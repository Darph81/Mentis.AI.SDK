namespace Mentis.AI.Sdk;

/// <summary>
/// Start conversations, link documents and chat. Obtain an instance via <see cref="IMentisClient.Conversations"/>.
/// </summary>
/// <remarks>
/// The instance from <see cref="IMentisClient.Conversations"/> works with tenant-global conversations,
/// visible to every user of the tenant. Use <see cref="ForUser"/> to work with the conversations of one
/// end user of your application instead.
/// </remarks>
public interface IConversationsClient
{
    /// <summary>
    /// The end user this client is scoped to, or <see langword="null"/> for tenant-global conversations.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Returns a client scoped to one end user of your application. Conversations started through it are
    /// owned by that user. The id is supplied by your application and not verified by the Manager.
    /// </summary>
    /// <param name="userId">Your application's id for the end user.</param>
    IConversationsClient ForUser(string userId);

    /// <summary>Starts a new conversation.</summary>
    /// <param name="title">Display title.</param>
    /// <param name="documentIds">Documents to link right away. If any does not exist, nothing is created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Conversation> StartAsync(
        string title,
        IEnumerable<string>? documentIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a conversation including all its messages.</summary>
    Task<Conversation> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Gets several conversations (without messages) by id in one call.</summary>
    Task<IReadOnlyList<ConversationSummary>> GetManyAsync(
        IEnumerable<string> conversationIds,
        CancellationToken cancellationToken = default);

    /// <summary>Lists one page of conversations (without messages).</summary>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 20.</param>
    /// <param name="titleContains">Only return conversations whose title contains this text (case-insensitive).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PagedResult<ConversationSummary>> ListAsync(
        int? pageNumber = null,
        int? pageSize = null,
        string? titleContains = null,
        CancellationToken cancellationToken = default);

    /// <summary>Iterates over all conversations, fetching further pages as needed.</summary>
    /// <param name="titleContains">Only return conversations whose title contains this text (case-insensitive).</param>
    /// <param name="pageSize">Items fetched per call; the server default is used when omitted.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    IAsyncEnumerable<ConversationSummary> EnumerateAsync(
        string? titleContains = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gets all conversations a document is linked to.</summary>
    Task<IReadOnlyList<ConversationSummary>> GetLinkedToDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the title of a conversation.</summary>
    Task<Conversation> RenameAsync(string conversationId, string newTitle, CancellationToken cancellationToken = default);

    /// <summary>Deletes a conversation and its messages.</summary>
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Links a document to a conversation so answers can draw on it.</summary>
    Task LinkDocumentAsync(string conversationId, string documentId, CancellationToken cancellationToken = default);

    /// <summary>Links several documents to a conversation in one call.</summary>
    Task LinkDocumentsAsync(
        string conversationId,
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the link between a document and a conversation.</summary>
    Task UnlinkDocumentAsync(string conversationId, string documentId, CancellationToken cancellationToken = default);

    /// <summary>Sends a user message and returns the assistant's answer.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="content">The user's message.</param>
    /// <param name="model">
    /// Optional model override. Must be the deployment's default model or one of its allow-listed models.
    /// </param>
    /// <param name="odataSecret">
    /// Credential for the tenant's configured OData data source, used for this call only. Ignored when the
    /// tenant has no OData source. Never stored or logged.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The assistant's answer including citations.</returns>
    Task<ChatMessage> SendMessageAsync(
        string conversationId,
        string content,
        string? model = null,
        string? odataSecret = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists one page of the messages of a conversation.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 50.</param>
    /// <param name="role">Only return messages with this role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PagedResult<ChatMessage>> ListMessagesAsync(
        string conversationId,
        int? pageNumber = null,
        int? pageSize = null,
        MessageRole? role = null,
        CancellationToken cancellationToken = default);

    /// <summary>Exports a conversation as a Markdown document.</summary>
    Task<string> ExportMarkdownAsync(string conversationId, CancellationToken cancellationToken = default);
}
