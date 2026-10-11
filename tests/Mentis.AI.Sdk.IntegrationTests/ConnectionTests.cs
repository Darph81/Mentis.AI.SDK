using Grpc.Core;

namespace Mentis.AI.Sdk.IntegrationTests;

public class ConnectionTests : IntegrationTest
{
    [Test]
    public async Task ValidCredentials_ReachTheManager()
    {
        TenantUsage usage = await Client.Billing.GetUsageAsync(cancellationToken: Timeout);

        usage.TenantId.ShouldBe(TenantId);
        usage.Year.ShouldBe(DateTime.UtcNow.Year);
        usage.Month.ShouldBe(DateTime.UtcNow.Month);
    }

    [Test]
    public async Task UsageHistory_IsNewestFirst()
    {
        IReadOnlyList<TenantUsage> history = await Client.Billing.GetUsageHistoryAsync(Timeout);

        history.Select(u => (u.Year * 12) + u.Month).ShouldBeInOrder(SortDirection.Descending);
    }

    [Test]
    public async Task OwnedChannel_WithShortKeepAlive_StaysUsableAcrossIdleTime()
    {
        // The SDK's own channel (not the test's shared one) with HTTP/2 pings every second: the connection must
        // survive idle time and further calls (a ping on an idle connection must not break it).
        await using var client = new MentisClient(new MentisClientOptions
        {
            Endpoint = Endpoint,
            ApiKey = ApiKey,
            KeepAlivePingDelay = TimeSpan.FromSeconds(1),
        });

        (await client.Billing.GetUsageAsync(cancellationToken: Timeout)).TenantId.ShouldBe(TenantId);
        await Task.Delay(TimeSpan.FromSeconds(3), Timeout);
        (await client.Billing.GetUsageAsync(cancellationToken: Timeout)).TenantId.ShouldBe(TenantId);
    }

    [Test]
    public async Task WrongSecret_ThrowsUnauthenticated()
    {
        await using var client = new MentisClient(new MentisClientOptions
        {
            Endpoint = Endpoint,
            TenantId = TenantId,
            Secret = "definitely-not-the-secret",
        });

        MentisException ex = await Should.ThrowAsync<MentisException>(
            () => client.Billing.GetUsageAsync(cancellationToken: Timeout));

        ex.StatusCode.ShouldBe(MentisStatusCode.Unauthenticated);
        ex.Message.ShouldNotContain("definitely-not-the-secret");
    }
}
