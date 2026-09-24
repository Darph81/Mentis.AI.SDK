namespace Mentis.AI.Sdk;

/// <summary>
/// Upload, search and manage documents. Obtain an instance via <see cref="IMentisClient.Documents"/>.
/// </summary>
public interface IDocumentsClient
{
    /// <summary>Uploads a file from disk.</summary>
    /// <param name="filePath">Path of the file to upload.</param>
    /// <param name="title">Display title; defaults to the file name without extension.</param>
    /// <param name="type">File format; inferred from the file extension when omitted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The stored document. It is processed in the background - use <see cref="WaitUntilProcessedAsync"/>
    /// to wait until it can be searched.
    /// </returns>
    Task<Document> UploadAsync(
        string filePath,
        string? title = null,
        DocumentType? type = null,
        CancellationToken cancellationToken = default);

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
    Task<Document> UploadAsync(
        Stream content,
        string fileName,
        string? title = null,
        DocumentType? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a document by id.</summary>
    /// <exception cref="MentisException">With <c>NotFound</c> if the document does not exist or is not visible.</exception>
    Task<Document> GetAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>Gets several documents by id in one call.</summary>
    Task<IReadOnlyList<Document>> GetManyAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Lists one page of documents.</summary>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 20.</param>
    /// <param name="status">Only return documents in this state.</param>
    /// <param name="titleContains">Only return documents whose title contains this text (case-insensitive).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PagedResult<Document>> ListAsync(
        int? pageNumber = null,
        int? pageSize = null,
        DocumentStatus? status = null,
        string? titleContains = null,
        CancellationToken cancellationToken = default);

    /// <summary>Iterates over all documents, fetching further pages as needed.</summary>
    /// <param name="status">Only return documents in this state.</param>
    /// <param name="titleContains">Only return documents whose title contains this text (case-insensitive).</param>
    /// <param name="pageSize">Items fetched per call; the server default is used when omitted.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    IAsyncEnumerable<Document> EnumerateAsync(
        DocumentStatus? status = null,
        string? titleContains = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the title of a document.</summary>
    Task<Document> RenameAsync(string documentId, string newTitle, CancellationToken cancellationToken = default);

    /// <summary>Deletes a document.</summary>
    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>Deletes several documents in one call.</summary>
    Task DeleteManyAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>Semantic search over the chunks of ready documents.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="documentIds">Restrict the search to these documents; all ready documents when omitted.</param>
    /// <param name="topK">Maximum number of results; the server defaults to 5.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(
        string query,
        IEnumerable<string>? documentIds = null,
        int? topK = null,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads the original file of a document.</summary>
    Task<DocumentContent> GetContentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>Lists one page of the chunks a document was split into.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 50.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PagedResult<DocumentChunk>> GetChunksAsync(
        string documentId,
        int? pageNumber = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>Queues a failed document for processing again.</summary>
    Task<Document> RetryProcessingAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls a document until processing has finished, i.e. its status is
    /// <see cref="DocumentStatus.Ready"/> or <see cref="DocumentStatus.Failed"/>.
    /// </summary>
    /// <param name="documentId">The document to wait for.</param>
    /// <param name="pollInterval">Delay between polls; defaults to one second.</param>
    /// <param name="cancellationToken">Cancels waiting - use a timed token source to limit the wait.</param>
    /// <returns>The document in its final state. Check <see cref="Document.Status"/> for failure.</returns>
    Task<Document> WaitUntilProcessedAsync(
        string documentId,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
}
