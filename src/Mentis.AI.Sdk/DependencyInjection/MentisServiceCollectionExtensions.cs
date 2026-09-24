using Mentis.AI.Sdk;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Mentis.AI SDK in a service collection.</summary>
public static class MentisServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MentisClient"/> as a singleton, plus <see cref="DocumentsClient"/>,
    /// <see cref="ConversationsClient"/> (tenant-global) and <see cref="BillingClient"/> for direct injection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets endpoint and credentials.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddMentisClient(
        this IServiceCollection services,
        Action<MentisClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MentisClientOptions>().Configure(configure);
        services.TryAddSingleton(sp => new MentisClient(sp.GetRequiredService<IOptions<MentisClientOptions>>().Value));
        services.TryAddSingleton(sp => sp.GetRequiredService<MentisClient>().Documents);
        services.TryAddSingleton(sp => sp.GetRequiredService<MentisClient>().Conversations);
        services.TryAddSingleton(sp => sp.GetRequiredService<MentisClient>().Billing);

        return services;
    }
}
