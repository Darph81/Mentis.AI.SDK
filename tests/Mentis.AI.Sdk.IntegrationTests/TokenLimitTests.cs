using Grpc.Core;

namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Tests the monthly token limit with a second tenant whose limit is <c>0</c>, configured as
/// <c>MENTIS_LIMIT_API_KEY</c>. Costs no tokens: the Manager rejects the message before calling the LLM.
/// Ignored when that key is not set.
/// </summary>
public class TokenLimitTests : IntegrationTest
{
    private MentisClient _limited = null!;

    [OneTimeSetUp]
    public void OneTimeSetUpLimitedClient()
    {
        string? apiKey = Environment.GetEnvironmentVariable("MENTIS_LIMIT_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Ignore("MENTIS_LIMIT_API_KEY not set - token limit tests skipped.");
        }

        _limited = CreateClient(apiKey);
    }

    [OneTimeTearDown]
    public void OneTimeTearDownLimitedClient() => _limited?.Dispose();

    [Test]
    public async Task Usage_ReportsTheLimit()
    {
        TenantUsage usage = await _limited.Billing.GetUsageAsync(cancellationToken: Timeout);

        // A limit of 0 must not be confused with "no limit" (null).
        usage.MonthlyTokenLimit.ShouldBe(0);
        usage.TotalTokens.ShouldBe(0);
    }

    [Test]
    public async Task SendMessage_WhenLimitIsReached_ThrowsAndStoresNothing()
    {
        Conversation conversation = await StartConversationAsync(_limited.Conversations);

        MentisException ex = await Should.ThrowAsync<MentisException>(
            () => _limited.Conversations.SendMessageAsync(conversation.Id, "Hello?", cancellationToken: Timeout));

        ex.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        ex.ErrorCode.ShouldBe("Tenant.MonthlyTokenLimitReached");

        // The rejected message is not stored and costs nothing.
        (await _limited.Conversations.GetAsync(conversation.Id, Timeout)).Messages.ShouldBeEmpty();
        (await _limited.Billing.GetUsageAsync(cancellationToken: Timeout)).TotalTokens.ShouldBe(0);
    }

    [Test]
    public async Task NonChatOperations_StillWork()
    {
        Conversation conversation = await StartConversationAsync(_limited.Conversations);

        string newTitle = UniqueTitle();
        (await _limited.Conversations.RenameAsync(conversation.Id, newTitle, Timeout)).Title.ShouldBe(newTitle);

        // Reading documents works too - and never shows the main test tenant's documents (tenant isolation).
        (await _limited.Documents.ListAsync(cancellationToken: Timeout)).Items.ShouldAllBe(d => d.TenantId != TenantId);
    }
}
