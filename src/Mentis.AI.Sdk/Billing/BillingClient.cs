using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>
/// Token usage of the calling tenant. Obtain an instance via <see cref="MentisClient.Billing"/>.
/// </summary>
public sealed class BillingClient
{
    private readonly Proto.BillingService.BillingServiceClient _client;

    internal BillingClient(Proto.BillingService.BillingServiceClient client)
    {
        _client = client;
    }

    /// <summary>Gets the tenant's token usage for one month.</summary>
    /// <param name="year">Calendar year; defaults to the current year.</param>
    /// <param name="month">Calendar month (1-12); defaults to the current month.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<TenantUsage> GetUsageAsync(
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default)
    {
        var request = new Proto.GetTenantUsageRequest();
        if (year is { } y)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(y, nameof(year));
            request.Year = y;
        }

        if (month is { } m)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(m, 1, nameof(month));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(m, 12, nameof(month));
            request.Month = m;
        }

        Proto.GetTenantUsageResponse response = await RpcInvoker.InvokeAsync(
            _client.GetTenantUsageAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Usage.ToModel();
    }

    /// <summary>
    /// Gets the tenant's usage for every month with recorded usage, newest first.
    /// Months without usage have no entry.
    /// </summary>
    public async Task<IReadOnlyList<TenantUsage>> GetUsageHistoryAsync(CancellationToken cancellationToken = default)
    {
        Proto.ListTenantUsageHistoryResponse response = await RpcInvoker.InvokeAsync(
            _client.ListTenantUsageHistoryAsync(new Proto.ListTenantUsageHistoryRequest(), cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.History.Select(u => u.ToModel())];
    }
}
