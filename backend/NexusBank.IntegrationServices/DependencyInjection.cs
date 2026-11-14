using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using NexusBank.Application.Common.Interfaces;

namespace NexusBank.IntegrationServices;

public static class IntegrationServiceCollectionExtensions
{
    public static IServiceCollection AddNexusIntegrationServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddHttpClient("integration-probe", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "NexusBank-HealthProbe/1.0");
        }).AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 5;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("self-api", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddScoped<IIntegrationProbe, IntegrationProbe>();
        services.AddScoped<IApiInvoker, SelfApiInvoker>();

        return services;
    }
}
