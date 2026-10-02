using System.Text;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using NSubstitute;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Tests;

public class DocumentsClientTests
{
    private Proto.DocumentService.DocumentServiceClient _grpc = null!;
    private DocumentsClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _grpc = Substitute.For<Proto.DocumentService.DocumentServiceClient>();
        _client = new DocumentsClient(_grpc, TestIds.Tenant1);
    }

    [Test]
    public async Task GetAsync_MapsDocument()
    {
        var created = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        _grpc.GetDocumentByIdAsync(Arg.Any<Proto.GetDocumentByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetDocumentByIdResponse
            {
                Document = new Proto.Document
                {
                    Id = TestIds.Document1.ToString(),
                    Title = "Handbook",
                    FileName = "handbook.pdf",
                    Type = Proto.DocumentType.Pdf,
                    SizeInBytes = 42,
                    Status = Proto.DocumentStatus.Ready,
                    CreatedAt = Timestamp.FromDateTimeOffset(created),
                    ChunkCount = 7,
                    TenantId = TestIds.Tenant1.ToString(),
                },
            }));

        Document document = await _client.GetAsync(TestIds.Document1);

        document.Id.ShouldBe(TestIds.Document1);
        document.Type.ShouldBe(DocumentType.Pdf);
        document.Status.ShouldBe(DocumentStatus.Ready);
        document.CreatedAt.ShouldBe(created);
        document.ProcessedAt.ShouldBeNull();
        document.FailureReason.ShouldBeNull();
        document.ChunkCount.ShouldBe(7);
        document.TenantId.ShouldBe(TestIds.Tenant1);
        document.IsGlobal.ShouldBeFalse();
    }

    [Test]
    public async Task GetAsync_GlobalDocumentWithUnknownStatus_MapsToNullTenantAndUnknown()
    {
        _grpc.GetDocumentByIdAsync(Arg.Any<Proto.GetDocumentByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetDocumentByIdResponse
            {
                Document = new Proto.Document { Id = TestIds.Document1.ToString(), Status = (Proto.DocumentStatus)99 },
            }));

        Document document = await _client.GetAsync(TestIds.Document1);

        document.IsGlobal.ShouldBeTrue();
        document.Status.ShouldBe(DocumentStatus.Unknown);
    }

    [Test]
    public async Task GetAsync_ServerError_ThrowsMentisException()
    {
        _grpc.GetDocumentByIdAsync(Arg.Any<Proto.GetDocumentByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Failure<Proto.GetDocumentByIdResponse>(StatusCode.NotFound, "Document.NotFound: nope"));

        MentisException ex = await Should.ThrowAsync<MentisException>(() => _client.GetAsync(TestIds.Document1));

        ex.StatusCode.ShouldBe(StatusCode.NotFound);
        ex.ErrorCode.ShouldBe("Document.NotFound");
    }

    [Test]
    public async Task GetAsync_CancelledByCaller_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _grpc.GetDocumentByIdAsync(Arg.Any<Proto.GetDocumentByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Failure<Proto.GetDocumentByIdResponse>(StatusCode.Cancelled, "cancelled"));

        await Should.ThrowAsync<OperationCanceledException>(() => _client.GetAsync(TestIds.Document1, cts.Token));
    }

    [Test]
    public async Task GetAsync_EmptyId_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.GetAsync(Guid.Empty));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task UploadAsync_FromStream_InfersTypeAndTitle()
    {
        Proto.UploadDocumentRequest? sent = null;
        _grpc.UploadDocumentAsync(Arg.Do<Proto.UploadDocumentRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.UploadDocumentResponse { Document = new Proto.Document { Id = TestIds.Document1.ToString() } }));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("# Notes"));

        Document document = await _client.UploadAsync(stream, "meeting-notes.MD");

        document.Id.ShouldBe(TestIds.Document1);
        sent.ShouldNotBeNull();
        sent.Type.ShouldBe(Proto.DocumentType.Markdown);
        sent.Title.ShouldBe("meeting-notes");
        sent.FileName.ShouldBe("meeting-notes.MD");
        sent.SizeInBytes.ShouldBe(7);
        sent.Content.ToStringUtf8().ShouldBe("# Notes");
    }

    [Test]
    public async Task UploadAsync_UnknownExtensionWithoutType_Throws()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        await Should.ThrowAsync<ArgumentException>(() => _client.UploadAsync(stream, "data.bin"));
    }

    [Test]
    public async Task ListAsync_SendsFiltersAndMapsPage()
    {
        Proto.ListDocumentsRequest? sent = null;
        _grpc.ListDocumentsAsync(Arg.Do<Proto.ListDocumentsRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListDocumentsResponse
            {
                Documents = { new Proto.Document { Id = TestIds.Document1.ToString() } },
                TotalCount = 11,
                PageNumber = 2,
                PageSize = 5,
            }));

        PagedResult<Document> page = await _client.ListAsync(2, 5, DocumentStatus.Failed, "report");

        sent!.PageNumber.ShouldBe(2);
        sent.PageSize.ShouldBe(5);
        sent.StatusFilter.ShouldBe(Proto.DocumentStatus.Failed);
        sent.TitleContains.ShouldBe("report");
        page.Items.Count.ShouldBe(1);
        page.TotalCount.ShouldBe(11);
        page.HasNextPage.ShouldBeTrue();
    }

    [Test]
    [TestCase(null)]
    [TestCase(2)]
    public async Task EnumerateAsync_WalksAllPagesExactlyOnce(int? pageSize)
    {
        Guid[] all = [.. Enumerable.Range(1, 5).Select(i => Guid.Parse($"d0c00000-0000-0000-0000-00000000000{i}"))];
        var requests = new List<(int PageNumber, int PageSize)>();
        _grpc.ListDocumentsAsync(Arg.Any<Proto.ListDocumentsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var request = ci.Arg<Proto.ListDocumentsRequest>()!;
                requests.Add((request.PageNumber, request.PageSize));

                // Behaves like the Manager: the page number only counts when a page size is sent too;
                // otherwise page 1 with the default size (2 in this fake) is returned.
                bool explicitPage = request.PageNumber > 0 && request.PageSize > 0;
                int number = explicitPage ? request.PageNumber : 1;
                int size = explicitPage ? request.PageSize : 2;

                var response = new Proto.ListDocumentsResponse { TotalCount = all.Length, PageNumber = number, PageSize = size };
                response.Documents.AddRange(all.Skip((number - 1) * size).Take(size).Select(id => new Proto.Document { Id = id.ToString() }));
                return GrpcTestCalls.Success(response);
            });

        var ids = new List<Guid>();
        await foreach (Document document in _client.EnumerateAsync(pageSize: pageSize))
        {
            ids.Add(document.Id);
        }

        ids.ShouldBe(all);
        requests.Skip(1).ShouldAllBe(r => r.PageSize == 2);
    }

    [Test]
    public async Task ListAsync_PageNumberWithoutPageSize_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.ListAsync(pageNumber: 2));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(null, false, false)]
    [TestCase(QueryScope.Both, false, false)]
    [TestCase(QueryScope.Tenant, true, false)]
    [TestCase(QueryScope.Global, false, true)]
    public async Task ListAsync_Scope_SetsTenantOrGlobalFilter(QueryScope? scope, bool expectTenant, bool expectGlobal)
    {
        Proto.ListDocumentsRequest? sent = null;
        _grpc.ListDocumentsAsync(Arg.Do<Proto.ListDocumentsRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.ListDocumentsResponse { PageNumber = 1, PageSize = 20 }));

        await _client.ListAsync(scope: scope, textContains: "handbook");

        sent!.HasTenantId.ShouldBe(expectTenant);
        if (expectTenant)
        {
            sent.TenantId.ShouldBe(TestIds.Tenant1.ToString());
        }

        sent.GlobalOnly.ShouldBe(expectGlobal);
        sent.TextContains.ShouldBe("handbook");
    }

    [Test]
    public async Task ListAsync_UnknownScope_Throws()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _client.ListAsync(scope: QueryScope.Unknown));
    }

    [Test]
    public async Task GetContentAsync_ReturnsBytesAndFileName()
    {
        _grpc.GetDocumentContentAsync(Arg.Any<Proto.GetDocumentContentRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.GetDocumentContentResponse
            {
                FileName = "a.txt",
                Content = ByteString.CopyFromUtf8("hello"),
            }));

        DocumentContent content = await _client.GetContentAsync(TestIds.Document1);

        content.FileName.ShouldBe("a.txt");
        Encoding.UTF8.GetString(content.Content.Span).ShouldBe("hello");
    }

    [Test]
    public async Task WaitUntilProcessedAsync_PollsUntilReady()
    {
        _grpc.GetDocumentByIdAsync(Arg.Any<Proto.GetDocumentByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(
                GrpcTestCalls.Success(new Proto.GetDocumentByIdResponse { Document = new Proto.Document { Id = TestIds.Document1.ToString(), Status = Proto.DocumentStatus.Processing } }),
                GrpcTestCalls.Success(new Proto.GetDocumentByIdResponse { Document = new Proto.Document { Id = TestIds.Document1.ToString(), Status = Proto.DocumentStatus.Ready } }));

        Document document = await _client.WaitUntilProcessedAsync(TestIds.Document1, TimeSpan.FromMilliseconds(1));

        document.Status.ShouldBe(DocumentStatus.Ready);
        _grpc.ReceivedCalls().Count().ShouldBe(2);
    }

    [Test]
    public async Task SearchAsync_SendsQueryAndMapsResults()
    {
        Proto.SearchDocumentsRequest? sent = null;
        _grpc.SearchDocumentsAsync(Arg.Do<Proto.SearchDocumentsRequest>(r => sent = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcTestCalls.Success(new Proto.SearchDocumentsResponse
            {
                Results = { new Proto.SearchResult { DocumentId = TestIds.Document1.ToString(), ChunkId = TestIds.Chunk1.ToString(), Content = "text", Score = 0.9 } },
            }));

        IReadOnlyList<DocumentSearchResult> results = await _client.SearchAsync("policy", [TestIds.Document1], topK: 3);

        sent!.QueryText.ShouldBe("policy");
        sent.DocumentIds.ShouldBe([TestIds.Document1.ToString()]);
        sent.TopK.ShouldBe(3);
        results.Single().Score.ShouldBe(0.9);
        results.Single().ChunkId.ShouldBe(TestIds.Chunk1);
    }

    [Test]
    public async Task GetManyAsync_WithEmptyGuid_ThrowsBeforeCallingServer()
    {
        await Should.ThrowAsync<ArgumentException>(() => _client.GetManyAsync([TestIds.Document1, Guid.Empty]));

        _grpc.ReceivedCalls().ShouldBeEmpty();
    }
}
