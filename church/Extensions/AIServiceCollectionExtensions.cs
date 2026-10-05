using church.AIServices;
using church.AIServices.AI;

namespace church.Extensions
{
    public static class AIServiceCollectionExtensions
    {
        public static IServiceCollection AddAiFirestoreServices(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            services.AddMemoryCache();

            services.AddSingleton<
                IFirestoreSettingsService,
                FirestoreSettingsService>();

            services.AddScoped<
                ISubscriptionCalculationService,
                SubscriptionCalculationService>();

            services.AddScoped<
                IAttendanceCalculationService,
                AttendanceCalculationService>();

            services.AddScoped<
                ICurrentServiceCodeResolver,
                CurrentServiceCodeResolver>();

            configuration ??= new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("AI_PROVIDER_TIMEOUT_SECONDS") ?? configuration["AI_PROVIDER_TIMEOUT_SECONDS"], out var timeout) ? timeout : 20;
            var cooldownSeconds = int.TryParse(Environment.GetEnvironmentVariable("AI_PROVIDER_COOLDOWN_SECONDS") ?? configuration["AI_PROVIDER_COOLDOWN_SECONDS"], out var cooldown) ? cooldown : 30;
            var options = new AIProviderOptions
            {
                PrimaryProvider = Environment.GetEnvironmentVariable("AI_PRIMARY_PROVIDER") ?? configuration["AI_PRIMARY_PROVIDER"] ?? "groq",
                FallbackProvider = Environment.GetEnvironmentVariable("AI_FALLBACK_PROVIDER") ?? configuration["AI_FALLBACK_PROVIDER"] ?? "gemini",
                VerificationEnabled = bool.TryParse(Environment.GetEnvironmentVariable("AI_VERIFICATION_ENABLED") ?? configuration["AI_VERIFICATION_ENABLED"], out var verification) && verification,
                ProviderTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120)),
                Cooldown = TimeSpan.FromSeconds(Math.Clamp(cooldownSeconds, 5, 600)),
                GroqApiKey = ReadSetting(configuration, "GROQ_API_KEY"),
                GroqModel = Environment.GetEnvironmentVariable("GROQ_MODEL") ?? configuration["GROQ_MODEL"] ?? "openai/gpt-oss-120b",
                GeminiApiKey = ReadSetting(configuration, "GEMINI_API_KEY"),
                GeminiModel = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? configuration["GEMINI_MODEL"] ?? "gemini-2.0-flash"
            };
            services.AddSingleton(options);
            services.AddSingleton<IAIProvider, GroqAIProvider>();
            services.AddSingleton<IAIProvider, GeminiAIProvider>();
            services.AddSingleton<IAIOrchestrator, AIOrchestrator>();

            return services;
        }

        private static string? ReadSetting(IConfiguration configuration, string key)
        {
            var value = Environment.GetEnvironmentVariable(key) ?? configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            return ReadTextSettingFile($"{key}.txt");
        }

        private static string? ReadTextSettingFile(string fileName)
        {
            var paths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName)
            };

            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var value = File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }
    }
}
