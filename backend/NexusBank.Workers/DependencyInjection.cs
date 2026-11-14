using Microsoft.Extensions.DependencyInjection;

namespace NexusBank.Workers;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddNexusWorkers(this IServiceCollection services)
    {
        services.AddHostedService<OutboxPublisherWorker>();
        services.AddHostedService<IntegrationHealthWorker>();
        services.AddHostedService<TelemetryRetentionWorker>();
        services.AddHostedService<KafkaEventConsumerWorker>();
        return services;
    }
}
