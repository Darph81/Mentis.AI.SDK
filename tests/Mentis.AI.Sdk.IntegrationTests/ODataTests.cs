namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Chat with the tenant's OData data source (<c>odataSecret</c>). The OData source is configured by the Manager
/// administrator (admin panel), not through the SDK, so this test is opt-in: set <c>MENTIS_ODATA_NORTHWIND=1</c>
/// once the test tenant's OData endpoint points at
/// <c>https://services.odata.org/V3/Northwind/Northwind.svc/</c> (auth scheme <c>None</c>; the public service needs
/// no credential). The Manager fetches exactly that URL, so the context is the service document: the list of
/// entity sets (Categories, Customers, CustomerDemographics, ...). Calls the LLM and the public internet, so it is slow and costs tokens.
/// </summary>
[Category("Llm")]
public class ODataTests : IntegrationTest
{
    [Test]
    public async Task SendMessage_WithODataSource_AnswersFromTheODataData()
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

        // "CustomerDemographics" is an unusual name that only the OData context can supply.
        answer.Content.ShouldContain("CustomerDemographics", Case.Insensitive);
    }
}
