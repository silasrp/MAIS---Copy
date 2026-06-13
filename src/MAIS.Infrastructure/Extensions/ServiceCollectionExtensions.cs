using MAIS.Core.Abstractions;
using MAIS.Infrastructure.EventBus;
using Microsoft.Extensions.DependencyInjection;

namespace MAIS.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering MAIS Infrastructure services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Infrastructure-layer services:
    /// <list type="bullet">
    ///   <item><see cref="IEventBus"/> — in-memory event bus (singleton)</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddMaisInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        return services;
    }
}
