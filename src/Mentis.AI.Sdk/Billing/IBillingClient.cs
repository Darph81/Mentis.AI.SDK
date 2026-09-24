namespace Mentis.AI.Sdk;

/// <summary>
/// Token usage of the calling tenant. Obtain an instance via <see cref="IMentisClient.Billing"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>Gets the tenant's token usage for one month.</summary>
    /// <param name="year">Calendar year; defaults to the current year.</param>
    /// <param name="month">Calendar month (1-12); defaults to the current month.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<TenantUsage> GetUsageAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tenant's usage for every month with recorded usage, newest first.
    /// Months without usage have no entry.
    /// </summary>
    Task<IReadOnlyList<TenantUsage>> GetUsageHistoryAsync(CancellationToken cancellationToken = default);
}
