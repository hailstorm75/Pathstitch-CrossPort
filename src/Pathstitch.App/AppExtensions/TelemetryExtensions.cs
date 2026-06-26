using Microsoft.Extensions.DependencyInjection;

namespace Pathstitch.App.AppExtensions;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetry(this IServiceCollection services)
    {
        // Add file-based logging + console logging for debug
        services.AddLogging();

        return services;
    }
}