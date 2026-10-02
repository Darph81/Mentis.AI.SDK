using Google.Protobuf;

using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>gRPC-backed implementation of <see cref="IDocumentsClient"/>.</summary>
internal sealed class DocumentsClient : IDocumentsClient
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
    private readonly Guid _tenantId;

    /// <param name="client">The generated gRPC client.</param>
    /// <param name="tenantId">The calling tenant - needed for <see cref="QueryScope.Tenant"/> list filters.</param>
    internal DocumentsClient(Proto.DocumentService.DocumentServiceClient client, Guid tenantId)
    {
        _client = client;
        _tenantId = tenantId;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<Document> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);

        Proto.GetDocumentByIdResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentByIdAsync(new Proto.GetDocumentByIdRequest { DocumentId = documentId.ToString() }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetManyAsync(
        IEnumerable<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        var request = new Proto.GetDocumentsByIdsRequest();
        request.DocumentIds.AddRange(Ids.ToWire(documentIds));

        Proto.GetDocumentsByIdsResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentsByIdsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Documents.Select(d => d.ToModel())];
    }

    /// <inheritdoc />
    public async Task<PagedResult<Document>> ListAsync(
        int? pageNumber = null,
        int? pageSize = null,
        DocumentStatus? status = null,
        string? titleContains = null,
        string? textContains = null,
        QueryScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        Paging.ValidatePaging(pageNumber, pageSize);
        if (scope == QueryScope.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Use null or QueryScope.Both for no scope filter.");
        }

        var request = new Proto.ListDocumentsRequest
        {
            PageNumber = pageNumber ?? 0,
            PageSize = pageSize ?? 0,
            StatusFilter = (Proto.DocumentStatus)(status ?? DocumentStatus.Unknown),
            TitleContains = titleContains ?? string.Empty,
            TextContains = textContains ?? string.Empty,
        };

        if (scope == QueryScope.Tenant)
        {
            request.TenantId = _tenantId.ToString();
        }
        else if (scope == QueryScope.Global)
        {
            request.GlobalOnly = true;
        }

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

    /// <inheritdoc />
    public IAsyncEnumerable<Document> EnumerateAsync(
        DocumentStatus? status = null,
        string? titleContains = null,
        string? textContains = null,
        QueryScope? scope = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        Paging.ValidatePaging(pageNumber: null, pageSize);

        return Paging.EnumerateAsync(
            (page, size, ct) => ListAsync(page, size, status, titleContains, textContains, scope, ct),
            pageSize,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Document> RenameAsync(
        Guid documentId,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);

        var request = new Proto.RenameDocumentRequest { DocumentId = documentId.ToString(), NewTitle = newTitle };
        Proto.RenameDocumentResponse response = await RpcInvoker.InvokeAsync(
            _client.RenameDocumentAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);

        await RpcInvoker.InvokeAsync(
            _client.DeleteDocumentAsync(new Proto.DeleteDocumentRequest { DocumentId = documentId.ToString() }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteManyAsync(IEnumerable<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        var request = new Proto.DeleteDocumentsRequest();
        request.DocumentIds.AddRange(Ids.ToWire(documentIds));

        await RpcInvoker.InvokeAsync(
            _client.DeleteDocumentsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(
        string query,
        IEnumerable<Guid>? documentIds = null,
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
            request.DocumentIds.AddRange(Ids.ToWire(documentIds));
        }

        Proto.SearchDocumentsResponse response = await RpcInvoker.InvokeAsync(
            _client.SearchDocumentsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Results.Select(r => r.ToModel())];
    }

    /// <inheritdoc />
    public async Task<DocumentContent> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);

        Proto.GetDocumentContentResponse response = await RpcInvoker.InvokeAsync(
            _client.GetDocumentContentAsync(new Proto.GetDocumentContentRequest { DocumentId = documentId.ToString() }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return new DocumentContent { FileName = response.FileName, Content = response.Content.Memory };
    }

    /// <inheritdoc />
    public async Task<PagedResult<DocumentChunk>> GetChunksAsync(
        Guid documentId,
        int? pageNumber = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);
        Paging.ValidatePaging(pageNumber, pageSize);

        var request = new Proto.GetDocumentChunksRequest
        {
            DocumentId = documentId.ToString(),
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

    /// <inheritdoc />
    public async Task<Document> RetryProcessingAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);

        Proto.RetryDocumentProcessingResponse response = await RpcInvoker.InvokeAsync(
            _client.RetryDocumentProcessingAsync(new Proto.RetryDocumentProcessingRequest { DocumentId = documentId.ToString() }, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Document.ToModel();
    }

    /// <inheritdoc />
    public async Task<Document> WaitUntilProcessedAsync(
        Guid documentId,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);
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
