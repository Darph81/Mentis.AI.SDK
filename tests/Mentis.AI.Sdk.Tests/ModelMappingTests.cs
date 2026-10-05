using Google.Protobuf.WellKnownTypes;

using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Tests;

/// <summary>Mapping of generated proto messages to the public models - the parts the client tests do not hit.</summary>
public class ModelMappingTests
{
    [Test]
    public void Document_MapsOptionalFieldsWhenSet()
    {
        var processed = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

        Document document = new Proto.Document
        {
            Id = TestIds.Document1.ToString(),
            Status = Proto.DocumentStatus.Failed,
            FailureReason = "Could not extract text",
            ProcessedAt = Timestamp.FromDateTimeOffset(processed),
            TenantId = TestIds.Tenant1.ToString(),
            SizeInBytes = 1234,
            ChunkCount = 3,
        }.ToModel();

        document.FailureReason.ShouldBe("Could not extract text");
        document.ProcessedAt.ShouldBe(processed);
        document.TenantId.ShouldBe(TestIds.Tenant1);
        document.IsGlobal.ShouldBeFalse();
        document.SizeInBytes.ShouldBe(1234);
        document.ChunkCount.ShouldBe(3);
    }

    [Test]
    public void Document_WithoutOptionalFields_HasNulls()
    {
        Document document = new Proto.Document { Id = TestIds.Document1.ToString() }.ToModel();

        document.FailureReason.ShouldBeNull();
        document.ProcessedAt.ShouldBeNull();
        document.TenantId.ShouldBeNull();
        document.IsGlobal.ShouldBeTrue();
    }

    [Test]
    public void UnknownEnumValues_BecomeUnknown()
    {
        Document document = new Proto.Document { Id = TestIds.Document1.ToString(), Type = (Proto.DocumentType)42, Status = (Proto.DocumentStatus)42 }.ToModel();
        ChatMessage message = new Proto.ChatMessage { Id = TestIds.Message1.ToString(), Role = (Proto.MessageRole)42, QueryScope = (Proto.QueryScope)42 }.ToModel();

        document.Type.ShouldBe(DocumentType.Unknown);
        document.Status.ShouldBe(DocumentStatus.Unknown);
        message.Role.ShouldBe(MessageRole.Unknown);
        message.QueryScope.ShouldBe(QueryScope.Unknown);
    }

    [TestCase(1, DocumentType.Pdf)]
    [TestCase(2, DocumentType.Docx)]
    [TestCase(3, DocumentType.Markdown)]
    [TestCase(4, DocumentType.Html)]
    [TestCase(5, DocumentType.Txt)]
    [TestCase(0, DocumentType.Unknown)]
    public void DocumentType_ValuesMatchTheProto(int proto, DocumentType expected)
    {
        new Proto.Document { Id = TestIds.Document1.ToString(), Type = (Proto.DocumentType)proto }.ToModel().Type.ShouldBe(expected);
    }

    [TestCase(1, QueryScope.Global)]
    [TestCase(2, QueryScope.Tenant)]
    [TestCase(3, QueryScope.Both)]
    [TestCase(0, QueryScope.Unknown)]
    public void QueryScope_ValuesMatchTheProto(int proto, QueryScope expected)
    {
        new Proto.ChatMessage { Id = TestIds.Message1.ToString(), QueryScope = (Proto.QueryScope)proto }.ToModel().QueryScope.ShouldBe(expected);
    }

    [Test]
    public void Conversation_WithoutOwner_IsTenantGlobal()
    {
        Conversation conversation = new Proto.Conversation { Id = TestIds.Conversation1.ToString(), TenantId = TestIds.Tenant1.ToString() }.ToModel();
        ConversationSummary summary = new Proto.ConversationSummary { Id = TestIds.Conversation1.ToString(), TenantId = TestIds.Tenant1.ToString() }.ToModel();

        conversation.OwnerUserId.ShouldBeNull();
        summary.OwnerUserId.ShouldBeNull();
    }

    [Test]
    public void Citation_EmptyDocumentTitle_BecomesNull()
    {
        var citation = new Proto.Citation
        {
            DocumentId = TestIds.Document1.ToString(),
            ChunkId = TestIds.Chunk1.ToString(),
            Snippet = "text",
        }.ToModel();

        citation.DocumentTitle.ShouldBeNull();
        citation.Snippet.ShouldBe("text");
    }

    [Test]
    public void MissingTimestamp_MapsToDefault()
    {
        new Proto.Document { Id = TestIds.Document1.ToString() }.ToModel().CreatedAt.ShouldBe(default);
    }

    [Test]
    public void MalformedId_FromTheServer_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => new Proto.Document { Id = "not-a-guid" }.ToModel());
    }

    [Test]
    public void TenantUsage_MapsTheLimit()
    {
        TenantUsage unlimited = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString() }.ToModel();
        TenantUsage zero = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString(), MonthlyTokenLimit = 0 }.ToModel();

        unlimited.MonthlyTokenLimit.ShouldBeNull();
        zero.MonthlyTokenLimit.ShouldBe(0);
    }

    [TestCase(1, 20, 41, true)]
    [TestCase(2, 20, 40, false)]
    [TestCase(1, 20, 0, false)]
    [TestCase(1, 0, 5, false)]
    public void PagedResult_HasNextPage(int pageNumber, int pageSize, int totalCount, bool expected)
    {
        var page = new PagedResult<int> { Items = [], PageNumber = pageNumber, PageSize = pageSize, TotalCount = totalCount };

        page.HasNextPage.ShouldBe(expected);
    }
}
