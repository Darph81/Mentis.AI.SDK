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
                Conversation = new Proto.Conversation { Id = TestIds.Conversation1.ToString(), Title = "Chat", TenantId = TestIds.Tenant1.ToString(), LinkedDocumentIds = { TestIds.Document1.ToString() } },
            }));

        Conversation conversation = await _client.StartAsync("Chat", [TestIds.Document1]);

        sent!.HasUserId.ShouldBeFalse();
        sent.InitialDocumentIds.ShouldBe([TestIds.Document1.ToString()]);
        conversation.OwnerUserId.ShouldBeNull();
        conversation.LinkedDocumentIds.ShouldBe([TestIds.Document1]);
    }

    [Test]
    public async Task ForUser_SendsUserIdOnEveryCall()
    {
        Proto.StartConversationRequest? start = null;
        Proto.DeleteConversationRequest? delete = null;
        _grpc.StartConversationAsync(Arg.Do<Proto.StartConversationRequest>(r => start = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.StartConversationResponse { Conversation = new Proto.Conversation { Id = TestIds.Conversation1.ToString(), OwnerUserId = TestIds.User1.ToString() } }));
        _grpc.DeleteConversationAsync(Arg.Do<Proto.DeleteConversationRequest>(r => delete = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.DeleteConversationResponse()));

        IConversationsClient alice = _client.ForUser(TestIds.User1);
        Conversation conversation = await alice.StartAsync("Chat");
        await alice.DeleteAsync(TestIds.Conversation1);

        alice.UserId.ShouldBe(TestIds.User1);
        _client.UserId.ShouldBeNull();
        start!.UserId.ShouldBe(TestIds.User1.ToString());
        delete!.UserId.ShouldBe(TestIds.User1.ToString());
        conversation.OwnerUserId.ShouldBe(TestIds.User1);
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
                    Id = TestIds.Message1.ToString(),
                    Role = Proto.MessageRole.Assistant,
                    Content = "20 days.",
                    Citations =
                    {
                        new Proto.Citation { DocumentId = TestIds.Document1.ToString(), ChunkId = TestIds.Chunk1.ToString(), Snippet = "20 days", DocumentTitle = "Handbook" },
                        new Proto.Citation { DocumentId = TestIds.Document2.ToString(), ChunkId = TestIds.Chunk2.ToString(), Snippet = "other" },
                    },
                },
            }));

        ChatMessage answer = await _client.SendMessageAsync(TestIds.Conversation1, "Vacation?", model: "llama3", odataSecret: "s3cret");

        sent!.Model.ShouldBe("llama3");
        sent.OdataSecret.ShouldBe("s3cret");
        sent.HasUserId.ShouldBeFalse();
        answer.Role.ShouldBe(MessageRole.Assistant);
        answer.Citations[0].DocumentTitle.ShouldBe("Handbook");
        answer.Citations[1].DocumentTitle.ShouldBeNull();
        answer.Citations[0].DocumentId.ShouldBe(TestIds.Document1);
        answer.Citations[0].ChunkId.ShouldBe(TestIds.Chunk1);
    }

    [Test]
    public async Task SendMessageAsync_WithoutOptionalFields_LeavesThemUnset()
    {
        Proto.SendMessageRequest? sent = null;
        _grpc.SendMessageAsync(Arg.Do<Proto.SendMessageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.SendMessageResponse { Message = new Proto.ChatMessage { Id = TestIds.Message1.ToString() } }));

        await _client.SendMessageAsync(TestIds.Conversation1, "Hi");

        sent!.HasModel.ShouldBeFalse();
        sent.HasOdataSecret.ShouldBeFalse();
    }

    [Test]
    public async Task ListMessagesAsync_SendsRoleFilter()
    {
        Proto.ListMessagesRequest? sent = null;
        _grpc.ListMessagesAsync(Arg.Do<Proto.ListMessagesRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListMessagesResponse { TotalCount = 0, PageNumber = 1, PageSize = 50 }));

        PagedResult<ChatMessage> page = await _client.ListMessagesAsync(TestIds.Conversation1, role: MessageRole.User);

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

        string markdown = await _client.ExportMarkdownAsync(TestIds.Conversation1);

        markdown.ShouldBe("# Chat");
    }

    [Test]
    public void ForUser_EmptyUserId_Throws()
    {
        Should.Throw<ArgumentException>(() => _client.ForUser(Guid.Empty));
    }
}
