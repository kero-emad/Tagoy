using church.AIServices;

namespace church.Extensions
{
    public static class AIServiceCollectionExtensions
    {
        public static IServiceCollection AddAiFirestoreServices(
            this IServiceCollection services)
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

            return services;
        }
    }
}
