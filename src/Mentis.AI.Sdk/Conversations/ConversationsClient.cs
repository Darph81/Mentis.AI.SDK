using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>gRPC-backed implementation of <see cref="IConversationsClient"/>.</summary>
internal sealed class ConversationsClient : IConversationsClient
{
    private readonly Proto.ConversationService.ConversationServiceClient _client;

    internal ConversationsClient(Proto.ConversationService.ConversationServiceClient client, string? userId = null)
    {
        _client = client;
        UserId = userId;
    }

    /// <inheritdoc />
    public string? UserId { get; }

    /// <inheritdoc />
    public IConversationsClient ForUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!Guid.TryParse(userId, out _))
        {
            throw new ArgumentException("The Manager requires the end-user id to be a GUID.", nameof(userId));
        }

        return new ConversationsClient(_client, userId);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
