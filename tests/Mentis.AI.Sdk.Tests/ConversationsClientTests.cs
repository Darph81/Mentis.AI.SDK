using Google.Protobuf.WellKnownTypes;

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
                    QueryScope = Proto.QueryScope.Tenant,
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
        answer.QueryScope.ShouldBe(QueryScope.Tenant);
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
    public async Task GetAsync_ReturnsMessagesInChronologicalOrder()
    {
        var older = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        _grpc.GetConversationByIdAsync(Arg.Any<Proto.GetConversationByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetConversationByIdResponse
            {
                Conversation = new Proto.Conversation
                {
                    Id = TestIds.Conversation1.ToString(),
                    Messages =
                    {
                        new Proto.ChatMessage { Id = TestIds.Message1.ToString(), Role = Proto.MessageRole.Assistant, CreatedAt = Timestamp.FromDateTimeOffset(older.AddSeconds(5)) },
                        new Proto.ChatMessage { Id = TestIds.Message1.ToString(), Role = Proto.MessageRole.User, CreatedAt = Timestamp.FromDateTimeOffset(older) },
                    },
                },
            }));

        Conversation conversation = await _client.GetAsync(TestIds.Conversation1);

        conversation.Messages.Select(m => m.Role).ShouldBe([MessageRole.User, MessageRole.Assistant]);
    }

    [Test]
    public void ForUser_EmptyUserId_Throws()
    {
        Should.Throw<ArgumentException>(() => _client.ForUser(Guid.Empty));
    }

    private IConversationsClient Alice => _client.ForUser(TestIds.User1);

    [Test]
    public async Task LinkDocumentAsync_SendsIdsAndUserId()
    {
        Proto.LinkDocumentToConversationRequest? sent = null;
        _grpc.LinkDocumentToConversationAsync(Arg.Do<Proto.LinkDocumentToConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.LinkDocumentToConversationResponse()));

        await Alice.LinkDocumentAsync(TestIds.Conversation1, TestIds.Document1);

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.DocumentId.ShouldBe(TestIds.Document1.ToString());
        sent.UserId.ShouldBe(TestIds.User1.ToString());
    }

    [Test]
    public async Task LinkDocumentAsync_TenantGlobal_DoesNotSendUserId()
    {
        Proto.LinkDocumentToConversationRequest? sent = null;
        _grpc.LinkDocumentToConversationAsync(Arg.Do<Proto.LinkDocumentToConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.LinkDocumentToConversationResponse()));

        await _client.LinkDocumentAsync(TestIds.Conversation1, TestIds.Document1);

        sent!.HasUserId.ShouldBeFalse();
    }

    [Test]
    public async Task LinkDocumentsAsync_SendsAllIdsAndUserId()
    {
        Proto.LinkDocumentsToConversationRequest? sent = null;
        _grpc.LinkDocumentsToConversationAsync(Arg.Do<Proto.LinkDocumentsToConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.LinkDocumentsToConversationResponse()));

        await Alice.LinkDocumentsAsync(TestIds.Conversation1, [TestIds.Document1, TestIds.Document2]);

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.DocumentIds.ShouldBe([TestIds.Document1.ToString(), TestIds.Document2.ToString()]);
        sent.UserId.ShouldBe(TestIds.User1.ToString());
    }

    [Test]
    public async Task UnlinkDocumentAsync_SendsIdsAndUserId()
    {
        Proto.UnlinkDocumentFromConversationRequest? sent = null;
        _grpc.UnlinkDocumentFromConversationAsync(Arg.Do<Proto.UnlinkDocumentFromConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.UnlinkDocumentFromConversationResponse()));

        await Alice.UnlinkDocumentAsync(TestIds.Conversation1, TestIds.Document1);

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.DocumentId.ShouldBe(TestIds.Document1.ToString());
        sent.UserId.ShouldBe(TestIds.User1.ToString());
    }

    [Test]
    public async Task GetLinkedToDocumentAsync_SendsIdsAndMapsSummaries()
    {
        Proto.GetConversationsLinkedToDocumentRequest? sent = null;
        _grpc.GetConversationsLinkedToDocumentAsync(Arg.Do<Proto.GetConversationsLinkedToDocumentRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetConversationsLinkedToDocumentResponse
            {
                Conversations =
                {
                    new Proto.ConversationSummary
                    {
                        Id = TestIds.Conversation1.ToString(),
                        Title = "Linked",
                        MessageCount = 4,
                        TenantId = TestIds.Tenant1.ToString(),
                        LinkedDocumentIds = { TestIds.Document1.ToString() },
                        OwnerUserId = TestIds.User1.ToString(),
                    },
                },
            }));

        IReadOnlyList<ConversationSummary> summaries = await Alice.GetLinkedToDocumentAsync(TestIds.Document1);

        sent!.DocumentId.ShouldBe(TestIds.Document1.ToString());
        sent.UserId.ShouldBe(TestIds.User1.ToString());
        ConversationSummary summary = summaries.Single();
        summary.Id.ShouldBe(TestIds.Conversation1);
        summary.Title.ShouldBe("Linked");
        summary.MessageCount.ShouldBe(4);
        summary.TenantId.ShouldBe(TestIds.Tenant1);
        summary.LinkedDocumentIds.ShouldBe([TestIds.Document1]);
        summary.OwnerUserId.ShouldBe(TestIds.User1);
    }

    [Test]
    public async Task GetManyAsync_SendsIdsAndMapsSummaries()
    {
        Proto.GetConversationsByIdsRequest? sent = null;
        _grpc.GetConversationsByIdsAsync(Arg.Do<Proto.GetConversationsByIdsRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetConversationsByIdsResponse
            {
                Conversations =
                {
                    new Proto.ConversationSummary { Id = TestIds.Conversation1.ToString(), TenantId = TestIds.Tenant1.ToString() },
                    new Proto.ConversationSummary { Id = TestIds.Conversation2.ToString(), TenantId = TestIds.Tenant1.ToString() },
                },
            }));

        IReadOnlyList<ConversationSummary> summaries = await Alice.GetManyAsync([TestIds.Conversation1, TestIds.Conversation2]);

        sent!.ConversationIds.ShouldBe([TestIds.Conversation1.ToString(), TestIds.Conversation2.ToString()]);
        sent.UserId.ShouldBe(TestIds.User1.ToString());
        summaries.Select(c => c.Id).ShouldBe([TestIds.Conversation1, TestIds.Conversation2]);
    }

    [Test]
    public async Task RenameAsync_SendsIdTitleAndUserIdAndMapsConversation()
    {
        Proto.RenameConversationRequest? sent = null;
        _grpc.RenameConversationAsync(Arg.Do<Proto.RenameConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.RenameConversationResponse
            {
                Conversation = new Proto.Conversation { Id = TestIds.Conversation1.ToString(), Title = "Renamed", TenantId = TestIds.Tenant1.ToString() },
            }));

        Conversation renamed = await Alice.RenameAsync(TestIds.Conversation1, "Renamed");

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.NewTitle.ShouldBe("Renamed");
        sent.UserId.ShouldBe(TestIds.User1.ToString());
        renamed.Title.ShouldBe("Renamed");
    }

    [TestCase("")]
    [TestCase("  ")]
    public async Task RenameAsync_BlankTitle_ThrowsBeforeCallingServer(string title)
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.RenameAsync(TestIds.Conversation1, title));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task StartAsync_BlankTitle_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.StartAsync(" "));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task StartAsync_WithEmptyDocumentGuid_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.StartAsync("Chat", [TestIds.Document1, Guid.Empty]));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task ListAsync_SendsFiltersAndUserIdAndMapsPage()
    {
        Proto.ListConversationsRequest? sent = null;
        _grpc.ListConversationsAsync(Arg.Do<Proto.ListConversationsRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListConversationsResponse
            {
                Conversations = { new Proto.ConversationSummary { Id = TestIds.Conversation1.ToString(), TenantId = TestIds.Tenant1.ToString() } },
                TotalCount = 3,
                PageNumber = 2,
                PageSize = 1,
            }));

        PagedResult<ConversationSummary> page = await Alice.ListAsync(pageNumber: 2, pageSize: 1, titleContains: "onboarding");

        sent!.PageNumber.ShouldBe(2);
        sent.PageSize.ShouldBe(1);
        sent.TitleContains.ShouldBe("onboarding");
        sent.UserId.ShouldBe(TestIds.User1.ToString());
        page.Items.Single().Id.ShouldBe(TestIds.Conversation1);
        page.TotalCount.ShouldBe(3);
        page.HasNextPage.ShouldBeTrue();
    }

    [Test]
    public async Task ListAsync_PageNumberWithoutPageSize_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.ListAsync(pageNumber: 2));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(null)]
    [TestCase(2)]
    public async Task EnumerateAsync_WalksAllPagesExactlyOnce(int? pageSize)
    {
        Guid[] all = [.. Enumerable.Range(1, 5).Select(i => Guid.Parse($"c0c00000-0000-0000-0000-00000000010{i}"))];
        var requests = new List<(int PageNumber, int PageSize)>();
        _grpc.ListConversationsAsync(Arg.Any<Proto.ListConversationsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var request = ci.Arg<Proto.ListConversationsRequest>()!;
                requests.Add((request.PageNumber, request.PageSize));

                // Behaves like the Manager: page number only counts together with a page size.
                bool explicitPage = request.PageNumber > 0 && request.PageSize > 0;
                int number = explicitPage ? request.PageNumber : 1;
                int size = explicitPage ? request.PageSize : 2;

                var response = new Proto.ListConversationsResponse { TotalCount = all.Length, PageNumber = number, PageSize = size };
                response.Conversations.AddRange(all.Skip((number - 1) * size).Take(size)
                    .Select(id => new Proto.ConversationSummary { Id = id.ToString(), TenantId = TestIds.Tenant1.ToString() }));
                return GrpcTestCalls.Success(response);
            });

        var ids = new List<Guid>();
        await foreach (ConversationSummary conversation in _client.EnumerateAsync(pageSize: pageSize))
        {
            ids.Add(conversation.Id);
        }

        ids.ShouldBe(all);
        requests.Skip(1).ShouldAllBe(r => r.PageSize == 2);
    }

    [Test]
    public async Task EnumerateAsync_NoResults_ReturnsNothing()
    {
        _grpc.ListConversationsAsync(Arg.Any<Proto.ListConversationsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListConversationsResponse { PageNumber = 1, PageSize = 20 }));

        var count = 0;
        await foreach (ConversationSummary _ in _client.EnumerateAsync())
        {
            count++;
        }

        count.ShouldBe(0);
        _grpc.ReceivedCalls().Count().ShouldBe(1);
    }

    [Test]
    public async Task ListMessagesAsync_SendsPagingAndUserIdAndMapsMessages()
    {
        Proto.ListMessagesRequest? sent = null;
        _grpc.ListMessagesAsync(Arg.Do<Proto.ListMessagesRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListMessagesResponse
            {
                Messages = { new Proto.ChatMessage { Id = TestIds.Message1.ToString(), Role = Proto.MessageRole.User, Content = "Hi" } },
                TotalCount = 1,
                PageNumber = 3,
                PageSize = 10,
            }));

        PagedResult<ChatMessage> page = await Alice.ListMessagesAsync(TestIds.Conversation1, pageNumber: 3, pageSize: 10);

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.PageNumber.ShouldBe(3);
        sent.PageSize.ShouldBe(10);
        sent.RoleFilter.ShouldBe(Proto.MessageRole.Unspecified);
        sent.UserId.ShouldBe(TestIds.User1.ToString());
        page.Items.Single().Content.ShouldBe("Hi");
        page.HasNextPage.ShouldBeFalse();
    }

    [Test]
    public async Task ListMessagesAsync_PageNumberWithoutPageSize_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.ListMessagesAsync(TestIds.Conversation1, pageNumber: 2));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task ExportMarkdownAsync_SendsIdAndUserId()
    {
        Proto.ExportConversationRequest? sent = null;
        _grpc.ExportConversationAsync(Arg.Do<Proto.ExportConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ExportConversationResponse { Markdown = "# Chat" }));

        await Alice.ExportMarkdownAsync(TestIds.Conversation1);

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.UserId.ShouldBe(TestIds.User1.ToString());
    }

    [Test]
    public async Task GetAsync_SendsUserId()
    {
        Proto.GetConversationByIdRequest? sent = null;
        _grpc.GetConversationByIdAsync(Arg.Do<Proto.GetConversationByIdRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetConversationByIdResponse
            {
                Conversation = new Proto.Conversation { Id = TestIds.Conversation1.ToString(), TenantId = TestIds.Tenant1.ToString() },
            }));

        await Alice.GetAsync(TestIds.Conversation1);

        sent!.UserId.ShouldBe(TestIds.User1.ToString());
    }

    [Test]
    public async Task GetAsync_MapsLinkedDocumentsOwnerAndMessages()
    {
        _grpc.GetConversationByIdAsync(Arg.Any<Proto.GetConversationByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetConversationByIdResponse
            {
                Conversation = new Proto.Conversation
                {
                    Id = TestIds.Conversation1.ToString(),
                    Title = "Chat",
                    TenantId = TestIds.Tenant1.ToString(),
                    OwnerUserId = TestIds.User1.ToString(),
                    LinkedDocumentIds = { TestIds.Document1.ToString(), TestIds.Document2.ToString() },
                },
            }));

        Conversation conversation = await Alice.GetAsync(TestIds.Conversation1);

        conversation.Title.ShouldBe("Chat");
        conversation.TenantId.ShouldBe(TestIds.Tenant1);
        conversation.OwnerUserId.ShouldBe(TestIds.User1);
        conversation.LinkedDocumentIds.ShouldBe([TestIds.Document1, TestIds.Document2]);
        conversation.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task SendMessageAsync_WithUserScope_SendsUserId()
    {
        Proto.SendMessageRequest? sent = null;
        _grpc.SendMessageAsync(Arg.Do<Proto.SendMessageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.SendMessageResponse { Message = new Proto.ChatMessage { Id = TestIds.Message1.ToString() } }));

        await Alice.SendMessageAsync(TestIds.Conversation1, "Hi");

        sent!.UserId.ShouldBe(TestIds.User1.ToString());
    }

    [Test]
    public async Task SendMessageAsync_BlankModelOrSecret_AreNotSent()
    {
        Proto.SendMessageRequest? sent = null;
        _grpc.SendMessageAsync(Arg.Do<Proto.SendMessageRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.SendMessageResponse { Message = new Proto.ChatMessage { Id = TestIds.Message1.ToString() } }));

        await _client.SendMessageAsync(TestIds.Conversation1, "Hi", model: "  ", odataSecret: "");

        sent!.HasModel.ShouldBeFalse();
        sent.HasOdataSecret.ShouldBeFalse();
    }

    [Test]
    public async Task SendMessageAsync_BlankContent_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.SendMessageAsync(TestIds.Conversation1, "  "));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task SendMessageAsync_ServerError_ThrowsMentisExceptionWithErrorCode()
    {
        _grpc.SendMessageAsync(Arg.Any<Proto.SendMessageRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Failure<Proto.SendMessageResponse>(StatusCode.FailedPrecondition, "Tenant.MonthlyTokenLimitReached: Limit reached."));

        MentisException ex = await Should.ThrowAsync<MentisException>(() => _client.SendMessageAsync(TestIds.Conversation1, "Hi"));

        ex.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        ex.ErrorCode.ShouldBe("Tenant.MonthlyTokenLimitReached");
    }

    [Test]
    public void ForUser_ReturnsScopedClientWithoutChangingTheOriginal()
    {
        IConversationsClient alice = _client.ForUser(TestIds.User1);

        alice.UserId.ShouldBe(TestIds.User1);
        _client.UserId.ShouldBeNull();
        alice.ShouldNotBeSameAs(_client);
    }

    private static IEnumerable<TestCaseData> EmptyIdCalls()
    {
        yield return Case("GetAsync", c => c.GetAsync(Guid.Empty));
        yield return Case("GetManyAsync", c => c.GetManyAsync([Guid.Empty]));
        yield return Case("GetLinkedToDocumentAsync", c => c.GetLinkedToDocumentAsync(Guid.Empty));
        yield return Case("RenameAsync", c => c.RenameAsync(Guid.Empty, "title"));
        yield return Case("DeleteAsync", c => c.DeleteAsync(Guid.Empty));
        yield return Case("LinkDocumentAsync (conversation)", c => c.LinkDocumentAsync(Guid.Empty, TestIds.Document1));
        yield return Case("LinkDocumentAsync (document)", c => c.LinkDocumentAsync(TestIds.Conversation1, Guid.Empty));
        yield return Case("LinkDocumentsAsync", c => c.LinkDocumentsAsync(TestIds.Conversation1, [Guid.Empty]));
        yield return Case("UnlinkDocumentAsync (conversation)", c => c.UnlinkDocumentAsync(Guid.Empty, TestIds.Document1));
        yield return Case("UnlinkDocumentAsync (document)", c => c.UnlinkDocumentAsync(TestIds.Conversation1, Guid.Empty));
        yield return Case("SendMessageAsync", c => c.SendMessageAsync(Guid.Empty, "Hi"));
        yield return Case("ListMessagesAsync", c => c.ListMessagesAsync(Guid.Empty));
        yield return Case("ExportMarkdownAsync", c => c.ExportMarkdownAsync(Guid.Empty));

        static TestCaseData Case(string name, Func<IConversationsClient, Task> call) => new TestCaseData(call).SetName(name);
    }

    [TestCaseSource(nameof(EmptyIdCalls))]
    public async Task EveryMethod_RejectsGuidEmpty_BeforeCallingTheServer(Func<IConversationsClient, Task> call)
    {
        await Should.ThrowAsync<ArgumentException>(() => call(_client));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task DeleteAsync_SendsIdAndUserId()
    {
        Proto.DeleteConversationRequest? sent = null;
        _grpc.DeleteConversationAsync(Arg.Do<Proto.DeleteConversationRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.DeleteConversationResponse()));

        await _client.DeleteAsync(TestIds.Conversation1);

        sent!.ConversationId.ShouldBe(TestIds.Conversation1.ToString());
        sent.HasUserId.ShouldBeFalse();
    }
}
