using Grpc.Core;

namespace Mentis.AI.Sdk.IntegrationTests;

public class ConversationTests : IntegrationTest
{
    [Test]
    public async Task Lifecycle_WithoutLlm()
    {
        Document document = await UploadTextAsync("Conversation lifecycle document.");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);
        IConversationsClient conversations = Client.Conversations;

        Conversation started = await StartConversationAsync(conversations, [document.Id]);
        started.LinkedDocumentIds.ShouldBe([document.Id]);
        started.OwnerUserId.ShouldBeNull();
        started.Messages.ShouldBeEmpty();

        await conversations.UnlinkDocumentAsync(started.Id, document.Id, Timeout);
        (await conversations.GetAsync(started.Id, Timeout)).LinkedDocumentIds.ShouldBeEmpty();

        await conversations.LinkDocumentAsync(started.Id, document.Id, Timeout);
        (await conversations.GetLinkedToDocumentAsync(document.Id, Timeout)).Single().Id.ShouldBe(started.Id);

        await conversations.UnlinkDocumentAsync(started.Id, document.Id, Timeout);
        await conversations.LinkDocumentsAsync(started.Id, [document.Id], Timeout);
        (await conversations.GetAsync(started.Id, Timeout)).LinkedDocumentIds.ShouldBe([document.Id]);

        string newTitle = UniqueTitle();
        (await conversations.RenameAsync(started.Id, newTitle, Timeout)).Title.ShouldBe(newTitle);

        (await conversations.GetManyAsync([started.Id], Timeout)).Single().Title.ShouldBe(newTitle);
        PagedResult<ConversationSummary> page = await conversations.ListAsync(titleContains: newTitle, cancellationToken: Timeout);
        page.Items.Single().MessageCount.ShouldBe(0);

        (await conversations.ListMessagesAsync(started.Id, cancellationToken: Timeout)).Items.ShouldBeEmpty();
        (await conversations.ExportMarkdownAsync(started.Id, Timeout)).ShouldContain(newTitle);

        await conversations.DeleteAsync(started.Id, Timeout);
        MentisException ex = await Should.ThrowAsync<MentisException>(() => conversations.GetAsync(started.Id, Timeout));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async Task StartWithUnknownDocument_ThrowsNotFound()
    {
        MentisException ex = await Should.ThrowAsync<MentisException>(
            () => Client.Conversations.StartAsync(UniqueTitle(), [Guid.NewGuid()], Timeout));

        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async Task UserScopedConversation_IsOnlyVisibleToThatUser()
    {
        IConversationsClient alice = Client.Conversations.ForUser(Guid.NewGuid());
        IConversationsClient bob = Client.Conversations.ForUser(Guid.NewGuid());

        Conversation owned = await StartConversationAsync(alice);
        owned.OwnerUserId.ShouldBe(alice.UserId);

        (await alice.GetAsync(owned.Id, Timeout)).Id.ShouldBe(owned.Id);
        (await Should.ThrowAsync<MentisException>(() => bob.GetAsync(owned.Id, Timeout))).StatusCode.ShouldBe(StatusCode.NotFound);
        (await Should.ThrowAsync<MentisException>(() => Client.Conversations.GetAsync(owned.Id, Timeout))).StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Test]
    public async Task TenantGlobalConversation_IsVisibleToEveryUser()
    {
        Conversation shared = await StartConversationAsync(Client.Conversations);
        IConversationsClient alice = Client.Conversations.ForUser(Guid.NewGuid());

        (await alice.GetAsync(shared.Id, Timeout)).Id.ShouldBe(shared.Id);
    }
}
