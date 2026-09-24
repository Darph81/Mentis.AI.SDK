using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>gRPC-backed implementation of <see cref="IBillingClient"/>.</summary>
internal sealed class BillingClient : IBillingClient
{
    private readonly Proto.BillingService.BillingServiceClient _client;

    internal BillingClient(Proto.BillingService.BillingServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantUsage>> GetUsageHistoryAsync(CancellationToken cancellationToken = default)
    {
        Proto.ListTenantUsageHistoryResponse response = await RpcInvoker.InvokeAsync(
            _client.ListTenantUsageHistoryAsync(new Proto.ListTenantUsageHistoryRequest(), cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.History.Select(u => u.ToModel())];
    }
}
