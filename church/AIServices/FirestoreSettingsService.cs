using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.Json;
using church.Models.AI;

namespace church.AIServices
{
    public interface IFirestoreSettingsService
    {
        Task<SubscriptionServiceSettingsDocument?> GetSubscriptionSettingsAsync(string serviceCode, CancellationToken cancellationToken = default);
        Task<AttendanceServiceSettingsDocument?> GetAttendanceSettingsAsync(string serviceCode, CancellationToken cancellationToken = default);
        Task<SettingsRefreshResult> RefreshServiceSettingsAsync(string serviceCode, CancellationToken cancellationToken = default);
        SettingsCacheStatus GetCacheStatus(string serviceCode);
    }

    public sealed class SettingsRefreshResult
    {
        public bool SubscriptionSettingsRefreshed { get; init; }
        public bool AttendanceSettingsRefreshed { get; init; }
        public bool SubscriptionDocumentFound { get; init; }
        public bool AttendanceDocumentFound { get; init; }
        public DateTime RefreshedAtUtc { get; init; }
    }

    public sealed class SettingsCacheStatus
    {
        public SettingsCacheEntryStatus Subscription { get; init; } = new();
        public SettingsCacheEntryStatus Attendance { get; init; } = new();
    }

    public sealed class SettingsCacheEntryStatus
    {
        public bool Cached { get; init; }
        public DateTime? LastLoadedAtUtc { get; init; }
        public DateTime? ExpiresAtUtc { get; init; }
        public bool? DocumentFound { get; init; }
    }

    public class FirestoreSettingsService : IFirestoreSettingsService
    {
        private const string SubscriptionType = "subscription";
        private const string AttendanceType = "attendance";
        private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromHours(24);
        private static readonly TimeSpan MissingDocumentLifetime = TimeSpan.FromMinutes(5);

        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheLifetime;
        private readonly Lazy<FirestoreDb> _firestoreDb;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly ConcurrentDictionary<string, CacheMetadata> _metadata = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastManualRefreshUtc = new();

        public FirestoreSettingsService(IMemoryCache cache)
        {
            _cache = cache;
            _cacheLifetime = ReadCacheLifetime();
            _firestoreDb = new Lazy<FirestoreDb>(BuildFirestoreDb);
        }

        public Task<SubscriptionServiceSettingsDocument?> GetSubscriptionSettingsAsync(string serviceCode, CancellationToken cancellationToken = default) =>
            GetAsync<SubscriptionServiceSettingsDocument>(serviceCode, SubscriptionType, cancellationToken);

        public Task<AttendanceServiceSettingsDocument?> GetAttendanceSettingsAsync(string serviceCode, CancellationToken cancellationToken = default) =>
            GetAsync<AttendanceServiceSettingsDocument>(serviceCode, AttendanceType, cancellationToken);

        public async Task<SettingsRefreshResult> RefreshServiceSettingsAsync(string serviceCode, CancellationToken cancellationToken = default)
        {
            serviceCode = NormalizeServiceCode(serviceCode);
            var refreshLock = _refreshLocks.GetOrAdd(serviceCode, _ => new SemaphoreSlim(1, 1));
            await refreshLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (_lastManualRefreshUtc.TryGetValue(serviceCode, out var lastRefresh) &&
                    now - lastRefresh < TimeSpan.FromSeconds(60))
                    throw new RefreshCooldownException(lastRefresh.AddSeconds(60));

                SubscriptionServiceSettingsDocument? subscription;
                try
                {
                    subscription = await LoadFromFirestoreAsync<SubscriptionServiceSettingsDocument>(serviceCode, SubscriptionType, cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new RefreshDocumentLoadException("subscription", ex);
                }

                AttendanceServiceSettingsDocument? attendance;
                try
                {
                    attendance = await LoadFromFirestoreAsync<AttendanceServiceSettingsDocument>(serviceCode, AttendanceType, cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new RefreshDocumentLoadException("attendance", ex);
                }

                Store(serviceCode, SubscriptionType, subscription, now);
                Store(serviceCode, AttendanceType, attendance, now);
                _lastManualRefreshUtc[serviceCode] = now;

                return new SettingsRefreshResult
                {
                    SubscriptionSettingsRefreshed = true,
                    AttendanceSettingsRefreshed = true,
                    SubscriptionDocumentFound = subscription != null,
                    AttendanceDocumentFound = attendance != null,
                    RefreshedAtUtc = now
                };
            }
            finally
            {
                refreshLock.Release();
            }
        }

        public SettingsCacheStatus GetCacheStatus(string serviceCode)
        {
            serviceCode = NormalizeServiceCode(serviceCode);
            return new SettingsCacheStatus
            {
                Subscription = GetEntryStatus(serviceCode, SubscriptionType),
                Attendance = GetEntryStatus(serviceCode, AttendanceType)
            };
        }

        private async Task<T?> GetAsync<T>(string serviceCode, string type, CancellationToken cancellationToken) where T : class
        {
            serviceCode = NormalizeServiceCode(serviceCode);
            var key = CacheKey(type, serviceCode);
            if (_cache.TryGetValue(key, out CachedDocument<T>? cached))
                return cached!.Document;

            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (_cache.TryGetValue(key, out cached))
                    return cached!.Document;

                var loadedAt = DateTime.UtcNow;
                var document = await LoadFromFirestoreAsync<T>(serviceCode, type, cancellationToken);
                Store(serviceCode, type, document, loadedAt);
                return document;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<T?> LoadFromFirestoreAsync<T>(string serviceCode, string type, CancellationToken cancellationToken) where T : class
        {
            var collection = type == SubscriptionType ? "subscriptionServiceSettings" : "attendanceServiceSettings";
            var snapshot = await _firestoreDb.Value.Collection(collection).Document(serviceCode).GetSnapshotAsync(cancellationToken);
            if (!snapshot.Exists)
                return null;

            var document = snapshot.ConvertTo<T>();
            if (document is SubscriptionServiceSettingsDocument subscription && string.IsNullOrWhiteSpace(subscription.ServiceCode))
                subscription.ServiceCode = serviceCode;
            else if (document is AttendanceServiceSettingsDocument attendance && string.IsNullOrWhiteSpace(attendance.ServiceCode))
                attendance.ServiceCode = serviceCode;
            return document;
        }

        private void Store<T>(string serviceCode, string type, T? document, DateTime loadedAtUtc) where T : class
        {
            var lifetime = document == null ? MissingDocumentLifetime : _cacheLifetime;
            var expiresAtUtc = loadedAtUtc.Add(lifetime);
            var key = CacheKey(type, serviceCode);
            _cache.Set(key, new CachedDocument<T>(document), new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = new DateTimeOffset(expiresAtUtc)
            });
            _metadata[key] = new CacheMetadata(loadedAtUtc, expiresAtUtc, document != null);
        }

        private SettingsCacheEntryStatus GetEntryStatus(string serviceCode, string type)
        {
            var key = CacheKey(type, serviceCode);
            _metadata.TryGetValue(key, out var metadata);
            var cached = metadata != null && metadata.ExpiresAtUtc > DateTime.UtcNow && _cache.TryGetValue(key, out _);
            return new SettingsCacheEntryStatus
            {
                Cached = cached,
                LastLoadedAtUtc = metadata?.LastLoadedAtUtc,
                ExpiresAtUtc = metadata?.ExpiresAtUtc,
                DocumentFound = metadata?.DocumentFound
            };
        }

        private static string CacheKey(string type, string serviceCode) => $"ai:firestore:{type}:{serviceCode}";

        private static string NormalizeServiceCode(string serviceCode)
        {
            if (string.IsNullOrWhiteSpace(serviceCode))
                throw new ArgumentException("serviceCode is required.", nameof(serviceCode));
            return serviceCode.Trim().ToUpperInvariant();
        }

        private static TimeSpan ReadCacheLifetime()
        {
            var raw = Environment.GetEnvironmentVariable("FIRESTORE_SETTINGS_CACHE_HOURS");
            return double.TryParse(raw, out var hours) && hours > 0 ? TimeSpan.FromHours(hours) : DefaultCacheLifetime;
        }

        private static FirestoreDb BuildFirestoreDb()
        {
            var projectId = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
            if (string.IsNullOrWhiteSpace(projectId))
                throw new InvalidOperationException("Missing FIREBASE_PROJECT_ID environment variable.");

            var credentialsFile = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
            if (!string.IsNullOrWhiteSpace(credentialsFile))
                return FirestoreDb.Create(projectId);

            var clientEmail = Environment.GetEnvironmentVariable("FIREBASE_CLIENT_EMAIL");
            var privateKey = Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY");
            if (string.IsNullOrWhiteSpace(clientEmail) || string.IsNullOrWhiteSpace(privateKey))
                throw new InvalidOperationException("Configure GOOGLE_APPLICATION_CREDENTIALS or FIREBASE_CLIENT_EMAIL + FIREBASE_PRIVATE_KEY.");

            privateKey = privateKey.Replace("\\n", "\n");
            var credentialJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "service_account",
                ["project_id"] = projectId,
                ["private_key"] = privateKey,
                ["client_email"] = clientEmail,
                ["token_uri"] = "https://oauth2.googleapis.com/token"
            });
            var credential = GoogleCredential.FromJson(credentialJson);
            var client = new FirestoreClientBuilder { Credential = credential }.Build();
            return FirestoreDb.Create(projectId, client);
        }

        private sealed record CachedDocument<T>(T? Document) where T : class;
        private sealed record CacheMetadata(DateTime LastLoadedAtUtc, DateTime ExpiresAtUtc, bool DocumentFound);

        public sealed class RefreshCooldownException : Exception
        {
            public RefreshCooldownException(DateTime nextRefreshAllowedAtUtc) : base("Refresh is on cooldown.") =>
                NextRefreshAllowedAtUtc = nextRefreshAllowedAtUtc;
            public DateTime NextRefreshAllowedAtUtc { get; }
        }

        public sealed class RefreshDocumentLoadException : Exception
        {
            public RefreshDocumentLoadException(string documentType, Exception innerException)
                : base($"Unable to load the {documentType} settings document.", innerException)
            {
                DocumentType = documentType;
            }

            public string DocumentType { get; }
        }
    }
}
