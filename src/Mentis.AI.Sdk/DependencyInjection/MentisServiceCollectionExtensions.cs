using Mentis.AI.Sdk;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Mentis.AI SDK in a service collection.</summary>
public static class MentisServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MentisClient"/> as a singleton for the given Manager and tenant, plus
    /// <see cref="DocumentsClient"/>, <see cref="ConversationsClient"/> (tenant-global) and
    /// <see cref="BillingClient"/> for direct injection. The credentials are configured once here and
    /// sent with every call - no request needs a tenant id.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoint">Address of the Mentis.AI Manager, e.g. <c>http://localhost:8080</c>.</param>
    /// <param name="tenantId">The tenant id issued by the Manager administrator.</param>
    /// <param name="secret">The tenant secret issued by the Manager administrator.</param>
    /// <param name="configure">Optional further settings such as <see cref="MentisClientOptions.Timeout"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddMentisClient(
        this IServiceCollection services,
        Uri endpoint,
        string tenantId,
        string secret,
        Action<MentisClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return services.AddMentisClient(options =>
        {
            options.Endpoint = endpoint;
            options.TenantId = tenantId;
            options.Secret = secret;
            configure?.Invoke(options);
        });
    }

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
