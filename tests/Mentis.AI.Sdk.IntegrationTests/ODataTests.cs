namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Chat with the tenant's OData data source (opt-in per message via <c>odataSecret</c>). The OData source is configured by the Manager
/// administrator (admin panel), not through the SDK, so this test is opt-in: set <c>MENTIS_ODATA_NORTHWIND=1</c>
/// once the test tenant's OData endpoint points at
/// <c>https://services.odata.org/V3/Northwind/Northwind.svc/</c> (auth scheme <c>None</c>; the public service needs
/// no credential). The Manager fetches exactly that URL, so the context is the service document: the list of
/// entity sets (Categories, Customers, CustomerDemographics, ...). Calls the LLM and the public internet, so it is slow and costs tokens.
/// </summary>
/// <remarks>
/// The tests check the transport and the opt-in only: a call with <c>odataSecret</c> succeeds for a tenant with an OData source.
/// If the Manager cannot fetch the source, the whole call fails (<c>OData.RequestFailed</c>), so a successful
/// answer proves the fetch worked. Whether the answer <i>uses</i> the data is not asserted: the SDK cannot see the
/// prompt, and the small default model (<c>llama3.2:1b</c>) ignores injected context too often to be a reliable check.
/// </remarks>
[Category("Llm")]
public class ODataTests : IntegrationTest
{
    private const string Question = "Which collections (entity sets) does the OData service offer? List all of their names.";

    // The Manager queries the OData source only when the request carries odata_secret. The public Northwind service
    // has auth scheme None, so the presence of the field is the signal - an empty string opts in, and a credential
    // is accepted and ignored.
    [TestCase("")]
    [TestCase("unused-for-public-northwind")]
    public async Task SendMessage_WithODataSecret_QueriesTheSourceAndSucceeds(string odataSecret)
    {
        Conversation conversation = await RequireODataSetupAndStartConversationAsync();

        ChatMessage answer;
        try
        {
            answer = await Client.Conversations.SendMessageAsync(
                conversation.Id, Question, odataSecret: odataSecret, cancellationToken: Timeout);
        }
        catch (MentisException ex) when (ex.ErrorCode == "OData.RequestFailed")
        {
            Assert.Fail(
                "The Manager tried to fetch the OData source but could not (OData.RequestFailed) - the opt-in itself worked. " +
                "Check that the Manager can reach it over HTTPS, e.g. from inside its container: " +
                "docker exec <container> curl -sS -m 15 -o /dev/null -w '%{http_code}' https://services.odata.org/ . " + ex.Message);
            throw;
        }

        TestContext.Out.WriteLine(answer.Content);
        answer.Role.ShouldBe(MessageRole.Assistant);
        answer.Content.ShouldNotBeNullOrWhiteSpace();
    }

    // Before the Manager made OData opt-in per message, a tenant with an OData source triggered a request on every
    // chat - and a failing source took the whole chat down. Without odataSecret the source must not be involved, so
    // this passes even if the Manager cannot reach the source at all.
    [Test]
    public async Task SendMessage_WithoutODataSecret_DoesNotNeedTheODataSource()
    {
        Conversation conversation = await RequireODataSetupAndStartConversationAsync();

        ChatMessage answer = await Client.Conversations.SendMessageAsync(
            conversation.Id, "Say hello in one short sentence.", cancellationToken: Timeout);

        answer.Role.ShouldBe(MessageRole.Assistant);
        answer.Content.ShouldNotBeNullOrWhiteSpace();
    }

    private async Task<Conversation> RequireODataSetupAndStartConversationAsync()
    {
        if (Environment.GetEnvironmentVariable("MENTIS_ODATA_NORTHWIND") is not "1")
        {
            Assert.Ignore("MENTIS_ODATA_NORTHWIND=1 not set - the test tenant has no Northwind OData source configured.");
        }

        TenantUsage usage = await Client.Billing.GetUsageAsync(cancellationToken: Timeout);
        if (usage.MonthlyTokenLimit is { } limit && usage.TotalTokens >= limit)
        {
            Assert.Ignore($"The test tenant's monthly token limit ({limit}) is used up - raise it to run chat tests.");
        }

        return await StartConversationAsync(Client.Conversations);
    }
}
