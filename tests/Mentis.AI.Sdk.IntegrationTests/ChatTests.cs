namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>Tests that call the Manager's LLM. They are slow and consume tokens.</summary>
[Category("Llm")]
public class ChatTests : IntegrationTest
{
    [Test]
    public async Task SendMessage_AnswersWithCitations()
    {
        TenantUsage usage = await Client.Billing.GetUsageAsync(cancellationToken: Timeout);
        if (usage.MonthlyTokenLimit is { } limit && usage.TotalTokens >= limit)
        {
            Assert.Ignore($"The test tenant's monthly token limit ({limit}) is used up - raise it to run chat tests.");
        }

        Document document = await UploadTextAsync(
            "Contoso travel policy: hotel costs are reimbursed up to 150 EUR per night.",
            "Contoso Travel Policy");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);
        Conversation conversation = await StartConversationAsync(Client.Conversations, [document.Id]);

        // Naming the tenant document's title steers the Manager's query-scope classifier towards the
        // tenant's own documents (Phase 72).
        const string question = "According to the Contoso Travel Policy, up to how much is a hotel night reimbursed?";
        ChatMessage answer = await Client.Conversations.SendMessageAsync(conversation.Id, question, cancellationToken: Timeout);

        answer.Role.ShouldBe(MessageRole.Assistant);
        answer.QueryScope.ShouldNotBe(QueryScope.Unknown);
        TestContext.Out.WriteLine($"Query scope: {answer.QueryScope}");
        answer.Content.ShouldNotBeNullOrWhiteSpace();
        // The Manager searches the tenant's own and/or the global documents (see QueryScope), never
        // only the linked ones, so other documents may be cited as well. Ours must be among them -
        // unless the classifier decided to search only the global documents.
        answer.Citations.ShouldAllBe(c => c.DocumentTitle != null);
        if (answer.QueryScope == QueryScope.Global)
        {
            answer.Citations.ShouldNotContain(c => c.DocumentId == document.Id);
        }
        else
        {
            answer.Citations.ShouldContain(c => c.DocumentId == document.Id && c.DocumentTitle == "Contoso Travel Policy");
        }

        // ListMessages is newest first; GetAsync returns the messages in chronological order.
        PagedResult<ChatMessage> messages = await Client.Conversations.ListMessagesAsync(conversation.Id, cancellationToken: Timeout);
        messages.Items.Select(m => m.Role).ShouldBe([MessageRole.Assistant, MessageRole.User]);
        (await Client.Conversations.GetAsync(conversation.Id, Timeout)).Messages
            .Select(m => m.Role).ShouldBe([MessageRole.User, MessageRole.Assistant]);

        PagedResult<ChatMessage> userOnly = await Client.Conversations.ListMessagesAsync(
            conversation.Id, role: MessageRole.User, cancellationToken: Timeout);
        userOnly.Items.Single().Content.ShouldBe(question);

        // Citations read back later carry no document title (Manager behavior).
        messages.Items[0].Citations.ShouldNotBeEmpty();
        messages.Items[0].Citations.ShouldAllBe(c => c.DocumentTitle == null);

        (await Client.Billing.GetUsageAsync(cancellationToken: Timeout)).TotalTokens.ShouldBeGreaterThan(0);
    }
}
