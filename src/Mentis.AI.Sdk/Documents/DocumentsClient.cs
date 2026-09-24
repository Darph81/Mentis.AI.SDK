using Google.Protobuf;

using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>
/// Upload, search and manage documents. Obtain an instance via <see cref="MentisClient.Documents"/>.
/// </summary>
public sealed class DocumentsClient
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<string, DocumentType> TypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = DocumentType.Pdf,
        [".docx"] = DocumentType.Docx,
        [".md"] = DocumentType.Markdown,
        [".markdown"] = DocumentType.Markdown,
        [".html"] = DocumentType.Html,
        [".htm"] = DocumentType.Html,
        [".txt"] = DocumentType.Txt,
    };

    private readonly Proto.DocumentService.DocumentServiceClient _client;

    internal DocumentsClient(Proto.DocumentService.DocumentServiceClient client)
    {
        _client = client;
    }

    /// <summary>Uploads a file from disk.</summary>
    /// <param name="filePath">Path of the file to upload.</param>
    /// <param name="title">Display title; defaults to the file name without extension.</param>
    /// <param name="type">File format; inferred from the file extension when omitted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The stored document. It is processed in the background - use <see cref="WaitUntilProcessedAsync"/>
    /// to wait until it can be searched.
    /// </returns>
    public async Task<Document> UploadAsync(
        string filePath,
        string? title = null,
        DocumentType? type = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FileStream stream = File.OpenRead(filePath);
        await using (stream.ConfigureAwait(false))
        {
            return await UploadAsync(stream, Path.GetFileName(filePath), title, type, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Uploads a document from a stream.</summary>
    /// <param name="content">The file content; read to the end, not disposed.</param>
    /// <param name="fileName">Original file name, e.g. <c>report.pdf</c>.</param>
    /// <param name="title">Display title; defaults to <paramref name="fileName"/> without extension.</param>
    /// <param name="type">File format; inferred from the extension of <paramref name="fileName"/> when omitted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The stored document. It is processed in the background - use <see cref="WaitUntilProcessedAsync"/>
    /// to wait until it can be searched.
    /// </returns>
    public async Task<Document> UploadAsync(
        Stream content,
        string fileName,
        string? title = null,
        DocumentType? type = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        DocumentType resolvedType = type ?? InferType(fileName);

        ByteString bytes = await ByteString.FromStreamAsync(content, cancellationToken).ConfigureAwait(false);
        var request = new Proto.UploadDocumentRequest
        {
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title,
            FileName = fileName,
            Type = (Proto.DocumentType)resolvedType,
            SizeInBytes = bytes.Length,
            Content = bytes,
        };

        Proto.UploadDocumentResponse response = await RpcInvoker.InvokeAsync(
            _client.UploadDocumentAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <summary>Gets a document by id.</summary>
    /// <exception cref="MentisException">With <c>NotFound</c> if the document does not exist or is not visible.</exception>
    public async Task<Document> GetAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        Proto.GetDocumentByIdResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentByIdAsync(new Proto.GetDocumentByIdRequest { DocumentId = documentId }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <summary>Gets several documents by id in one call.</summary>
    public async Task<IReadOnlyList<Document>> GetManyAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var request = new Proto.GetDocumentsByIdsRequest();
        request.DocumentIds.AddRange(documentIds);

        Proto.GetDocumentsByIdsResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentsByIdsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Documents.Select(d => d.ToModel())];
    }

    /// <summary>Lists one page of documents.</summary>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 20.</param>
    /// <param name="status">Only return documents in this state.</param>
    /// <param name="titleContains">Only return documents whose title contains this text (case-insensitive).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<PagedResult<Document>> ListAsync(
        int? pageNumber = null,
        int? pageSize = null,
        DocumentStatus? status = null,
        string? titleContains = null,
        CancellationToken cancellationToken = default)
    {
        Paging.ValidatePaging(pageNumber, pageSize);

        var request = new Proto.ListDocumentsRequest
        {
            PageNumber = pageNumber ?? 0,
            PageSize = pageSize ?? 0,
            StatusFilter = (Proto.DocumentStatus)(status ?? DocumentStatus.Unknown),
            TitleContains = titleContains ?? string.Empty,
        };

        Proto.ListDocumentsResponse response = await RpcInvoker.InvokeAsync(
            _client.ListDocumentsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<Document>
        {
            Items = [.. response.Documents.Select(d => d.ToModel())],
            TotalCount = response.TotalCount,
            PageNumber = response.PageNumber,
            PageSize = response.PageSize,
        };
    }

    /// <summary>Iterates over all documents, fetching further pages as needed.</summary>
    /// <param name="status">Only return documents in this state.</param>
    /// <param name="titleContains">Only return documents whose title contains this text (case-insensitive).</param>
    /// <param name="pageSize">Items fetched per call; the server default is used when omitted.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    public IAsyncEnumerable<Document> EnumerateAsync(
        DocumentStatus? status = null,
        string? titleContains = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        Paging.ValidatePaging(pageNumber: null, pageSize);

        return Paging.EnumerateAsync(
            (page, ct) => ListAsync(page, pageSize, status, titleContains, ct),
            cancellationToken);
    }

    /// <summary>Changes the title of a document.</summary>
    public async Task<Document> RenameAsync(
        string documentId,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);

        var request = new Proto.RenameDocumentRequest { DocumentId = documentId, NewTitle = newTitle };
        Proto.RenameDocumentResponse response = await RpcInvoker.InvokeAsync(
            _client.RenameDocumentAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <summary>Deletes a document.</summary>
    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        await RpcInvoker.InvokeAsync(
            _client.DeleteDocumentAsync(new Proto.DeleteDocumentRequest { DocumentId = documentId }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes several documents in one call.</summary>
    public async Task DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var request = new Proto.DeleteDocumentsRequest();
        request.DocumentIds.AddRange(documentIds);

        await RpcInvoker.InvokeAsync(
            _client.DeleteDocumentsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Semantic search over the chunks of ready documents.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="documentIds">Restrict the search to these documents; all ready documents when omitted.</param>
    /// <param name="topK">Maximum number of results; the server defaults to 5.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(
        string query,
        IEnumerable<string>? documentIds = null,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (topK is { } k)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k, nameof(topK));
        }

        var request = new Proto.SearchDocumentsRequest { QueryText = query, TopK = topK ?? 0 };
        if (documentIds is not null)
        {
            request.DocumentIds.AddRange(documentIds);
        }

        Proto.SearchDocumentsResponse response = await RpcInvoker.InvokeAsync(
            _client.SearchDocumentsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Results.Select(r => r.ToModel())];
    }

    /// <summary>Downloads the original file of a document.</summary>
    public async Task<DocumentContent> GetContentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        Proto.GetDocumentContentResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentContentAsync(new Proto.GetDocumentContentRequest { DocumentId = documentId }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return new DocumentContent { FileName = response.FileName, Content = response.Content.Memory };
    }

    /// <summary>Lists one page of the chunks a document was split into.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 50.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<PagedResult<DocumentChunk>> GetChunksAsync(
        string documentId,
        int? pageNumber = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        Paging.ValidatePaging(pageNumber, pageSize);

        var request = new Proto.GetDocumentChunksRequest
        {
            DocumentId = documentId,
            PageNumber = pageNumber ?? 0,
            PageSize = pageSize ?? 0,
        };

        Proto.GetDocumentChunksResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentChunksAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<DocumentChunk>
        {
            Items = [.. response.Chunks.Select(c => c.ToModel())],
            TotalCount = response.TotalCount,
            PageNumber = response.PageNumber,
            PageSize = response.PageSize,
        };
    }

    /// <summary>Queues a failed document for processing again.</summary>
    public async Task<Document> RetryProcessingAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        Proto.RetryDocumentProcessingResponse response = await RpcInvoker.InvokeAsync(
            _client.RetryDocumentProcessingAsync(new Proto.RetryDocumentProcessingRequest { DocumentId = documentId }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <summary>
    /// Polls a document until processing has finished, i.e. its status is
    /// <see cref="DocumentStatus.Ready"/> or <see cref="DocumentStatus.Failed"/>.
    /// </summary>
    /// <param name="documentId">The document to wait for.</param>
    /// <param name="pollInterval">Delay between polls; defaults to one second.</param>
    /// <param name="cancellationToken">Cancels waiting - use a timed token source to limit the wait.</param>
    /// <returns>The document in its final state. Check <see cref="Document.Status"/> for failure.</returns>
    public async Task<Document> WaitUntilProcessedAsync(
        string documentId,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        TimeSpan interval = pollInterval ?? DefaultPollInterval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(pollInterval));

        while (true)
        {
            Document document = await GetAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (document.Status is DocumentStatus.Ready or DocumentStatus.Failed)
            {
                return document;
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static DocumentType InferType(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return TypesByExtension.TryGetValue(extension, out DocumentType type)
            ? type
            : throw new ArgumentException(
                $"Cannot infer the document type from '{fileName}'. Pass the type explicitly.",
                nameof(fileName));
    }
}
