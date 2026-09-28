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

        ex.StatusCode.ShouldBe(StatusCode.Unauthenticated);
        ex.Message.ShouldNotContain("definitely-not-the-secret");
    }
}
