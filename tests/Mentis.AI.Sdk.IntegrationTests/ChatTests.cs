namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>Tests that call the Manager's LLM. They are slow and consume tokens.</summary>
[Category("Llm")]
public class ChatTests : IntegrationTest
{
    [Test]
    public async Task SendMessage_AnswersWithCitations()
    {
        Document document = await UploadTextAsync(
            "Contoso travel policy: hotel costs are reimbursed up to 150 EUR per night.",
            "Contoso Travel Policy");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);
        Conversation conversation = await StartConversationAsync(Client.Conversations, [document.Id]);

        ChatMessage answer = await Client.Conversations.SendMessageAsync(
            conversation.Id, "Up to how much is a hotel night reimbursed?", cancellationToken: Timeout);

        answer.Role.ShouldBe(MessageRole.Assistant);
        answer.Content.ShouldNotBeNullOrWhiteSpace();
        answer.Citations.ShouldNotBeEmpty();
        answer.Citations.ShouldAllBe(c => c.DocumentId == document.Id && c.DocumentTitle == "Contoso Travel Policy");

        PagedResult<ChatMessage> messages = await Client.Conversations.ListMessagesAsync(conversation.Id, cancellationToken: Timeout);
        messages.Items.Select(m => m.Role).ShouldBe([MessageRole.User, MessageRole.Assistant]);

        PagedResult<ChatMessage> userOnly = await Client.Conversations.ListMessagesAsync(
            conversation.Id, role: MessageRole.User, cancellationToken: Timeout);
        userOnly.Items.Single().Content.ShouldBe("Up to how much is a hotel night reimbursed?");

        // Citations read back later carry no document title (Manager behavior).
        messages.Items[1].Citations.ShouldAllBe(c => c.DocumentTitle == null);

        (await Client.Billing.GetUsageAsync(cancellationToken: Timeout)).TotalTokens.ShouldBeGreaterThan(0);
    }
}
