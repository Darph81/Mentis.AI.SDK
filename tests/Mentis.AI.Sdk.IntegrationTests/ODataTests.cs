namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Chat with the tenant's OData data source (<c>odataSecret</c>). The OData source is configured by the Manager
/// administrator (admin panel), not through the SDK, so this test is opt-in: set <c>MENTIS_ODATA_NORTHWIND=1</c>
/// once the test tenant's OData endpoint points at
/// <c>https://services.odata.org/V3/Northwind/Northwind.svc/</c> (auth scheme <c>None</c>; the public service needs
/// no credential). The Manager fetches exactly that URL, so the context is the service document: the list of
/// entity sets (Categories, Customers, CustomerDemographics, ...). Calls the LLM and the public internet, so it is slow and costs tokens.
/// </summary>
/// <remarks>
/// The test checks the transport only: a call with <c>odataSecret</c> succeeds for a tenant with an OData source.
/// If the Manager cannot fetch the source, the whole call fails (<c>OData.RequestFailed</c>), so a successful
/// answer proves the fetch worked. Whether the answer <i>uses</i> the data is not asserted: the SDK cannot see the
/// prompt, and the small default model (<c>llama3.2:1b</c>) ignores injected context too often to be a reliable check.
/// </remarks>
[Category("Llm")]
public class ODataTests : IntegrationTest
{
    [Test]
    public async Task SendMessage_WithODataSource_Succeeds()
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

        Conversation conversation = await StartConversationAsync(Client.Conversations);

        // The secret is ignored by the public Northwind service, but must be accepted and passed along.
        ChatMessage answer = await Client.Conversations.SendMessageAsync(
            conversation.Id,
            "Which collections (entity sets) does the OData service offer? List all of their names.",
            odataSecret: "unused-for-public-northwind",
            cancellationToken: Timeout);

        TestContext.Out.WriteLine(answer.Content);
        answer.Role.ShouldBe(MessageRole.Assistant);

        answer.Content.ShouldNotBeNullOrWhiteSpace();
    }
}
