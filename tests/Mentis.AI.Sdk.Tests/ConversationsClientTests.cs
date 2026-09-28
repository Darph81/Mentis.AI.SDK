using Grpc.Core;

using NSubstitute;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Tests;

public class ConversationsClientTests
{
    private Proto.ConversationService.ConversationServiceClient _grpc = null!;
    private ConversationsClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _grpc = Substitute.For<Proto.ConversationService.ConversationServiceClient>();
        _client = new ConversationsClient(_grpc);
    }

    [Test]
    public async Task StartAsync_TenantGlobal_DoesNotSendUserId()
    {
        Proto.StartConversationRequest? sent = null;
        _grpc.StartConversationAsync(Arg.Do<Proto.StartConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.StartConversationResponse
            {
                Conversation = new Proto.Conversation { Id = "c1", Title = "Chat", TenantId = "t1", LinkedDocumentIds = { "d1" } },
            }));

        Conversation conversation = await _client.StartAsync("Chat", ["d1"]);

        sent!.HasUserId.ShouldBeFalse();
        sent.InitialDocumentIds.ShouldBe(["d1"]);
        conversation.OwnerUserId.ShouldBeNull();
        conversation.LinkedDocumentIds.ShouldBe(["d1"]);
    }

    [Test]
    public async Task ForUser_SendsUserIdOnEveryCall()
    {
        Proto.StartConversationRequest? start = null;
        Proto.DeleteConversationRequest? delete = null;
        _grpc.StartConversationAsync(Arg.Do<Proto.StartConversationRequest>(r => start = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.StartConversationResponse { Conversation = new Proto.Conversation { Id = "c1", OwnerUserId = "5c3e8a4e-2f7b-4a51-9f0e-6f1d2b3c4d5e" } }));
        _grpc.DeleteConversationAsync(Arg.Do<Proto.DeleteConversationRequest>(r => delete = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.DeleteConversationResponse()));

        const string aliceId = "5c3e8a4e-2f7b-4a51-9f0e-6f1d2b3c4d5e";
        IConversationsClient alice = _client.ForUser(aliceId);
        Conversation conversation = await alice.StartAsync("Chat");
        await alice.DeleteAsync("c1");

        alice.UserId.ShouldBe(aliceId);
        _client.UserId.ShouldBeNull();
        start!.UserId.ShouldBe(aliceId);
        delete!.UserId.ShouldBe(aliceId);
        conversation.OwnerUserId.ShouldBe(aliceId);
    }

    [Test]
    public async Task SendMessageAsync_SendsOptionalFieldsAndMapsCitations()
    {
        Proto.SendMessageRequest? sent = null;
        _grpc.SendMessageAsync(Arg.Do<Proto.SendMessageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.SendMessageResponse
            {
                Message = new Proto.ChatMessage
                {
                    Id = "m1",
                    Role = Proto.MessageRole.Assistant,
                    Content = "20 days.",
                    Citations =
                    {
                        new Proto.Citation { DocumentId = "d1", ChunkId = "k1", Snippet = "20 days", DocumentTitle = "Handbook" },
                        new Proto.Citation { DocumentId = "d2", ChunkId = "k2", Snippet = "other" },
                    },
                },
            }));

        ChatMessage answer = await _client.SendMessageAsync("c1", "Vacation?", model: "llama3", odataSecret: "s3cret");

        sent!.Model.ShouldBe("llama3");
        sent.OdataSecret.ShouldBe("s3cret");
        sent.HasUserId.ShouldBeFalse();
        answer.Role.ShouldBe(MessageRole.Assistant);
        answer.Citations[0].DocumentTitle.ShouldBe("Handbook");
        answer.Citations[1].DocumentTitle.ShouldBeNull();
    }

    [Test]
    public async Task SendMessageAsync_WithoutOptionalFields_LeavesThemUnset()
    {
        Proto.SendMessageRequest? sent = null;
        _grpc.SendMessageAsync(Arg.Do<Proto.SendMessageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.SendMessageResponse { Message = new Proto.ChatMessage { Id = "m1" } }));

        await _client.SendMessageAsync("c1", "Hi");

        sent!.HasModel.ShouldBeFalse();
        sent.HasOdataSecret.ShouldBeFalse();
    }

    [Test]
    public async Task ListMessagesAsync_SendsRoleFilter()
    {
        Proto.ListMessagesRequest? sent = null;
        _grpc.ListMessagesAsync(Arg.Do<Proto.ListMessagesRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListMessagesResponse { TotalCount = 0, PageNumber = 1, PageSize = 50 }));

        PagedResult<ChatMessage> page = await _client.ListMessagesAsync("c1", role: MessageRole.User);

        sent!.RoleFilter.ShouldBe(Proto.MessageRole.User);
        sent.PageNumber.ShouldBe(0);
        page.Items.ShouldBeEmpty();
        page.HasNextPage.ShouldBeFalse();
    }

    [Test]
    public async Task ExportMarkdownAsync_ReturnsMarkdown()
    {
        _grpc.ExportConversationAsync(Arg.Any<Proto.ExportConversationRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ExportConversationResponse { Markdown = "# Chat" }));

        string markdown = await _client.ExportMarkdownAsync("c1");

        markdown.ShouldBe("# Chat");
    }

    [Test]
    public void ForUser_EmptyUserId_Throws()
    {
        Should.Throw<ArgumentException>(() => _client.ForUser(""));
    }

    [Test]
    public void ForUser_NonGuidUserId_Throws()
    {
        Should.Throw<ArgumentException>(() => _client.ForUser("alice"));
    }
}
