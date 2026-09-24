using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>
/// Start conversations, link documents and chat. Obtain an instance via <see cref="MentisClient.Conversations"/>.
/// </summary>
/// <remarks>
/// The instance from <see cref="MentisClient.Conversations"/> works with tenant-global conversations,
/// visible to every user of the tenant. Use <see cref="ForUser"/> to work with the conversations of one
/// end user of your application instead.
/// </remarks>
public sealed class ConversationsClient
{
    private readonly Proto.ConversationService.ConversationServiceClient _client;

    internal ConversationsClient(Proto.ConversationService.ConversationServiceClient client, string? userId = null)
    {
        _client = client;
        UserId = userId;
    }

    /// <summary>
    /// The end user this client is scoped to, or <see langword="null"/> for tenant-global conversations.
    /// </summary>
    public string? UserId { get; }

    /// <summary>
    /// Returns a client scoped to one end user of your application. Conversations started through it are
    /// owned by that user. The id is supplied by your application and not verified by the Manager.
    /// </summary>
    /// <param name="userId">Your application's id for the end user.</param>
    public ConversationsClient ForUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new ConversationsClient(_client, userId);
    }

    /// <summary>Starts a new conversation.</summary>
    /// <param name="title">Display title.</param>
    /// <param name="documentIds">Documents to link right away. If any does not exist, nothing is created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<Conversation> StartAsync(
        string title,
        IEnumerable<string>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var request = new Proto.StartConversationRequest { Title = title };
        if (documentIds is not null)
        {
            request.InitialDocumentIds.AddRange(documentIds);
        }

        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.StartConversationResponse response = await RpcInvoker.InvokeAsync(
            _client.StartConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Conversation.ToModel();
    }

    /// <summary>Gets a conversation including all its messages.</summary>
    public async Task<Conversation> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        var request = new Proto.GetConversationByIdRequest { ConversationId = conversationId };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.GetConversationByIdResponse response = await RpcInvoker.InvokeAsync(
            _client.GetConversationByIdAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Conversation.ToModel();
    }

    /// <summary>Gets several conversations (without messages) by id in one call.</summary>
    public async Task<IReadOnlyList<ConversationSummary>> GetManyAsync(
        IEnumerable<string> conversationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversationIds);

        var request = new Proto.GetConversationsByIdsRequest();
        request.ConversationIds.AddRange(conversationIds);
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.GetConversationsByIdsResponse response = await RpcInvoker.InvokeAsync(
            _client.GetConversationsByIdsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Conversations.Select(c => c.ToModel())];
    }

    /// <summary>Lists one page of conversations (without messages).</summary>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 20.</param>
    /// <param name="titleContains">Only return conversations whose title contains this text (case-insensitive).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<PagedResult<ConversationSummary>> ListAsync(
        int? pageNumber = null,
        int? pageSize = null,
        string? titleContains = null,
        CancellationToken cancellationToken = default)
    {
        Paging.ValidatePaging(pageNumber, pageSize);

        var request = new Proto.ListConversationsRequest
        {
            PageNumber = pageNumber ?? 0,
            PageSize = pageSize ?? 0,
            TitleContains = titleContains ?? string.Empty,
        };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.ListConversationsResponse response = await RpcInvoker.InvokeAsync(
            _client.ListConversationsAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<ConversationSummary>
        {
            Items = [.. response.Conversations.Select(c => c.ToModel())],
            TotalCount = response.TotalCount,
            PageNumber = response.PageNumber,
            PageSize = response.PageSize,
        };
    }

    /// <summary>Iterates over all conversations, fetching further pages as needed.</summary>
    /// <param name="titleContains">Only return conversations whose title contains this text (case-insensitive).</param>
    /// <param name="pageSize">Items fetched per call; the server default is used when omitted.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    public IAsyncEnumerable<ConversationSummary> EnumerateAsync(
        string? titleContains = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        Paging.ValidatePaging(pageNumber: null, pageSize);

        return Paging.EnumerateAsync(
            (page, ct) => ListAsync(page, pageSize, titleContains, ct),
            cancellationToken);
    }

    /// <summary>Gets all conversations a document is linked to.</summary>
    public async Task<IReadOnlyList<ConversationSummary>> GetLinkedToDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var request = new Proto.GetConversationsLinkedToDocumentRequest { DocumentId = documentId };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.GetConversationsLinkedToDocumentResponse response = await RpcInvoker.InvokeAsync(
            _client.GetConversationsLinkedToDocumentAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Conversations.Select(c => c.ToModel())];
    }

    /// <summary>Changes the title of a conversation.</summary>
    public async Task<Conversation> RenameAsync(
        string conversationId,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);

        var request = new Proto.RenameConversationRequest { ConversationId = conversationId, NewTitle = newTitle };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.RenameConversationResponse response = await RpcInvoker.InvokeAsync(
            _client.RenameConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Conversation.ToModel();
    }

    /// <summary>Deletes a conversation and its messages.</summary>
    public async Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        var request = new Proto.DeleteConversationRequest { ConversationId = conversationId };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        await RpcInvoker.InvokeAsync(
            _client.DeleteConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Links a document to a conversation so answers can draw on it.</summary>
    public async Task LinkDocumentAsync(
        string conversationId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var request = new Proto.LinkDocumentToConversationRequest { ConversationId = conversationId, DocumentId = documentId };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        await RpcInvoker.InvokeAsync(
            _client.LinkDocumentToConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Links several documents to a conversation in one call.</summary>
    public async Task LinkDocumentsAsync(
        string conversationId,
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(documentIds);

        var request = new Proto.LinkDocumentsToConversationRequest { ConversationId = conversationId };
        request.DocumentIds.AddRange(documentIds);
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        await RpcInvoker.InvokeAsync(
            _client.LinkDocumentsToConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the link between a document and a conversation.</summary>
    public async Task UnlinkDocumentAsync(
        string conversationId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var request = new Proto.UnlinkDocumentFromConversationRequest { ConversationId = conversationId, DocumentId = documentId };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        await RpcInvoker.InvokeAsync(
            _client.UnlinkDocumentFromConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

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
    public async Task<ChatMessage> SendMessageAsync(
        string conversationId,
        string content,
        string? model = null,
        string? odataSecret = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var request = new Proto.SendMessageRequest { ConversationId = conversationId, Content = content };
        if (!string.IsNullOrWhiteSpace(model))
        {
            request.Model = model;
        }

        if (!string.IsNullOrEmpty(odataSecret))
        {
            request.OdataSecret = odataSecret;
        }

        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.SendMessageResponse response = await RpcInvoker.InvokeAsync(
            _client.SendMessageAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Message.ToModel();
    }

    /// <summary>Lists one page of the messages of a conversation.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="pageNumber">1-based page number; the server defaults to 1.</param>
    /// <param name="pageSize">Items per page; the server defaults to 50.</param>
    /// <param name="role">Only return messages with this role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<PagedResult<ChatMessage>> ListMessagesAsync(
        string conversationId,
        int? pageNumber = null,
        int? pageSize = null,
        MessageRole? role = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        Paging.ValidatePaging(pageNumber, pageSize);

        var request = new Proto.ListMessagesRequest
        {
            ConversationId = conversationId,
            PageNumber = pageNumber ?? 0,
            PageSize = pageSize ?? 0,
            RoleFilter = (Proto.MessageRole)(role ?? MessageRole.Unknown),
        };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.ListMessagesResponse response = await RpcInvoker.InvokeAsync(
            _client.ListMessagesAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<ChatMessage>
        {
            Items = [.. response.Messages.Select(m => m.ToModel())],
            TotalCount = response.TotalCount,
            PageNumber = response.PageNumber,
            PageSize = response.PageSize,
        };
    }

    /// <summary>Exports a conversation as a Markdown document.</summary>
    public async Task<string> ExportMarkdownAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        var request = new Proto.ExportConversationRequest { ConversationId = conversationId };
        if (UserId is not null)
        {
            request.UserId = UserId;
        }

        Proto.ExportConversationResponse response = await RpcInvoker.InvokeAsync(
            _client.ExportConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Markdown;
    }
}
