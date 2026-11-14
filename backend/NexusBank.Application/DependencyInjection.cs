using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Services;

namespace NexusBank.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddNexusApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddScoped<AuthService>();
        services.AddScoped<IntegrationService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<EventStreamService>();
        services.AddScoped<ObservabilityService>();
        services.AddScoped<IncidentService>();
        services.AddScoped<SecurityService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<SystemHealthService>();
        services.AddScoped<ApiExplorerService>();
        services.AddScoped<CoreBankingService>();

        services.AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly, includeInternalTypes: true);
        return services;
    }
}
