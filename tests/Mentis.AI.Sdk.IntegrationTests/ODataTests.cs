namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Chat with the tenant's OData data source (<c>odataSecret</c>). The OData source is configured by the Manager
/// administrator (admin panel), not through the SDK, so this test is opt-in: set <c>MENTIS_ODATA_NORTHWIND=1</c>
/// once the test tenant's OData endpoint points at
/// <c>https://services.odata.org/V3/Northwind/Northwind.svc/Customers?$top=5</c> (auth scheme <c>None</c>; the
/// public service needs no credential). Calls the LLM and the public internet, so it is slow and costs tokens.
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
            "Who is the contact person of the customer Alfreds Futterkiste, and in which city is it located?",
            odataSecret: "unused-for-public-northwind",
            cancellationToken: Timeout);

        TestContext.Out.WriteLine(answer.Content);
        answer.Role.ShouldBe(MessageRole.Assistant);

        // Only the OData context knows this customer: Maria Anders, Berlin.
        answer.Content.ShouldContain("Maria Anders", Case.Insensitive);
    }
}
