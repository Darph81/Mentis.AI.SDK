using System.Text;

using Grpc.Core;

namespace Mentis.AI.Sdk.IntegrationTests;

public class DocumentTests : IntegrationTest
{
    [Test]
    public async Task Upload_IsProcessedToReady()
    {
        Document uploaded = await UploadTextAsync("The quick brown fox jumps over the lazy dog.");

        uploaded.Status.ShouldBeOneOf(DocumentStatus.Uploaded, DocumentStatus.Processing, DocumentStatus.Ready);
        uploaded.Type.ShouldBe(DocumentType.Txt);
        uploaded.IsGlobal.ShouldBeFalse();

        Document processed = await Client.Documents.WaitUntilProcessedAsync(uploaded.Id, cancellationToken: Timeout);

        processed.Status.ShouldBe(DocumentStatus.Ready, processed.FailureReason);
        processed.ChunkCount.ShouldBeGreaterThan(0);
        processed.ProcessedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task ReadOperations_ReturnTheDocument()
    {
        const string text = "Mentis integration test document about solar panels and battery storage.";
        Document document = await UploadTextAsync(text);
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);

        (await Client.Documents.GetAsync(document.Id, Timeout)).Title.ShouldBe(document.Title);
        (await Client.Documents.GetManyAsync([document.Id], Timeout)).Single().Id.ShouldBe(document.Id);

        PagedResult<Document> page = await Client.Documents.ListAsync(titleContains: document.Title, cancellationToken: Timeout);
        page.Items.Single().Id.ShouldBe(document.Id);
        page.TotalCount.ShouldBe(1);

        DocumentContent content = await Client.Documents.GetContentAsync(document.Id, Timeout);
        content.FileName.ShouldBe("sdk-integration-test.txt");
        Encoding.UTF8.GetString(content.Content.Span).ShouldStartWith(text);

        PagedResult<DocumentChunk> chunks = await Client.Documents.GetChunksAsync(document.Id, cancellationToken: Timeout);
        chunks.Items.ShouldNotBeEmpty();
        chunks.Items[0].Content.ShouldContain("solar panels");
    }

    [Test]
    public async Task Search_FindsTheDocument()
    {
        Document document = await UploadTextAsync("Photosynthesis converts light energy into chemical energy in plants.");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);

        IReadOnlyList<DocumentSearchResult> results = await Client.Documents.SearchAsync(
            "photosynthesis light energy", [document.Id], topK: 3, Timeout);

        results.ShouldNotBeEmpty();
        results.ShouldAllBe(r => r.DocumentId == document.Id);
    }

    [Test]
    public async Task Rename_ChangesTitle()
    {
        Document document = await UploadTextAsync("Rename me.");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);
        string newTitle = UniqueTitle();

        Document renamed = await Client.Documents.RenameAsync(document.Id, newTitle, Timeout);

        renamed.Title.ShouldBe(newTitle);
    }

    [Test]
    public async Task Delete_RemovesTheDocument()
    {
        Document document = await UploadTextAsync("Delete me.");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);

        await Client.Documents.DeleteAsync(document.Id, Timeout);

        MentisException ex = await Should.ThrowAsync<MentisException>(() => Client.Documents.GetAsync(document.Id, Timeout));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
        ex.ErrorCode.ShouldBe("Document.NotFound");
        ex.Message.ShouldContain(document.Id);
    }

    [Test]
    public async Task DeleteMany_RemovesAllDocuments()
    {
        Document first = await UploadTextAsync("First of two.");
        Document second = await UploadTextAsync("Second of two.");
        await Client.Documents.WaitUntilProcessedAsync(first.Id, cancellationToken: Timeout);
        await Client.Documents.WaitUntilProcessedAsync(second.Id, cancellationToken: Timeout);

        await Client.Documents.DeleteManyAsync([first.Id, second.Id], Timeout);

        (await Client.Documents.GetManyAsync([first.Id, second.Id], Timeout)).ShouldBeEmpty();
    }

    [Test]
    public async Task DuplicateContent_ThrowsFailedPrecondition()
    {
        string unique = $"Duplicate check {Guid.NewGuid()}";
        using var first = new MemoryStream(Encoding.UTF8.GetBytes(unique));
        Document document = await Client.Documents.UploadAsync(first, "duplicate.txt", cancellationToken: Timeout);
        TrackDocument(document.Id);

        using var second = new MemoryStream(Encoding.UTF8.GetBytes(unique));
        MentisException ex = await Should.ThrowAsync<MentisException>(
            () => Client.Documents.UploadAsync(second, "duplicate-again.txt", cancellationToken: Timeout));

        ex.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        ex.ErrorCode.ShouldBe("Document.DuplicateContent");
        ex.Message.ShouldContain(document.Id);
    }

    [Test]
    public async Task ServerValidation_ReturnsFieldErrors()
    {
        Document document = await UploadTextAsync("Validation target.");
        await Client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: Timeout);

        MentisException ex = await Should.ThrowAsync<MentisException>(
            () => Client.Documents.RenameAsync(document.Id, new string('x', 201), Timeout));

        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
        ex.ErrorCode.ShouldNotBeNull();
        ex.ValidationErrors.ShouldNotBeEmpty();
        TestContext.Out.WriteLine($"{ex.ErrorCode}: {ex.Message} | fields: {string.Join(", ", ex.ValidationErrors.Keys)}");
    }

    [Test]
    public async Task UploadAboveClientSendLimit_IsRejectedBeforeReachingServer()
    {
        await using var limited = new MentisClient(new MentisClientOptions
        {
            Endpoint = Endpoint,
            ApiKey = ApiKey,
            MaxSendMessageSizeBytes = 1024,
        });
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 4096) + Guid.NewGuid()));

        MentisException? ex = null;
        try
        {
            // Tracked in case the limit unexpectedly does not apply, so nothing is left behind.
            TrackDocument((await limited.Documents.UploadAsync(content, "too-large.txt", cancellationToken: Timeout)).Id);
        }
        catch (MentisException caught)
        {
            ex = caught;
        }

        ex.ShouldNotBeNull();
        ex.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
    }
}
