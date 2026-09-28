using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>gRPC-backed implementation of <see cref="IConversationsClient"/>.</summary>
internal sealed class ConversationsClient : IConversationsClient
{
    private readonly Proto.ConversationService.ConversationServiceClient _client;

    internal ConversationsClient(Proto.ConversationService.ConversationServiceClient client, Guid? userId = null)
    {
        _client = client;
        UserId = userId;
    }

    /// <inheritdoc />
    public Guid? UserId { get; }

    /// <inheritdoc />
    public IConversationsClient ForUser(Guid userId)
    {
        Ids.ThrowIfEmpty(userId);
        return new ConversationsClient(_client, userId);
    }

    /// <inheritdoc />
    public async Task<Conversation> StartAsync(
        string title,
        IEnumerable<Guid>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var request = new Proto.StartConversationRequest { Title = title };
        if (documentIds is not null)
        {
            request.InitialDocumentIds.AddRange(Ids.ToWire(documentIds));
        }

        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        Proto.StartConversationResponse response = await RpcInvoker.InvokeAsync(
            _client.StartConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Conversation.ToModel();
    }

    /// <inheritdoc />
    public async Task<Conversation> GetAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);

        var request = new Proto.GetConversationByIdRequest { ConversationId = conversationId.ToString() };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        Proto.GetConversationByIdResponse response = await RpcInvoker.InvokeAsync(
            _client.GetConversationByIdAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Conversation.ToModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> GetManyAsync(
        IEnumerable<Guid> conversationIds,
        CancellationToken cancellationToken = default)
    {
        var request = new Proto.GetConversationsByIdsRequest();
        request.ConversationIds.AddRange(Ids.ToWire(conversationIds));
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
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
            request.UserId = UserId.Value.ToString();
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
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(documentId);

        var request = new Proto.GetConversationsLinkedToDocumentRequest { DocumentId = documentId.ToString() };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        Proto.GetConversationsLinkedToDocumentResponse response = await RpcInvoker.InvokeAsync(
            _client.GetConversationsLinkedToDocumentAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return [.. response.Conversations.Select(c => c.ToModel())];
    }

    /// <inheritdoc />
    public async Task<Conversation> RenameAsync(
        Guid conversationId,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);

        var request = new Proto.RenameConversationRequest { ConversationId = conversationId.ToString(), NewTitle = newTitle };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        Proto.RenameConversationResponse response = await RpcInvoker.InvokeAsync(
            _client.RenameConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Conversation.ToModel();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);

        var request = new Proto.DeleteConversationRequest { ConversationId = conversationId.ToString() };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        await RpcInvoker.InvokeAsync(
            _client.DeleteConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LinkDocumentAsync(
        Guid conversationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);
        Ids.ThrowIfEmpty(documentId);

        var request = new Proto.LinkDocumentToConversationRequest { ConversationId = conversationId.ToString(), DocumentId = documentId.ToString() };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        await RpcInvoker.InvokeAsync(
            _client.LinkDocumentToConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LinkDocumentsAsync(
        Guid conversationId,
        IEnumerable<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);
        var request = new Proto.LinkDocumentsToConversationRequest { ConversationId = conversationId.ToString() };
        request.DocumentIds.AddRange(Ids.ToWire(documentIds));
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        await RpcInvoker.InvokeAsync(
            _client.LinkDocumentsToConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnlinkDocumentAsync(
        Guid conversationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);
        Ids.ThrowIfEmpty(documentId);

        var request = new Proto.UnlinkDocumentFromConversationRequest { ConversationId = conversationId.ToString(), DocumentId = documentId.ToString() };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        await RpcInvoker.InvokeAsync(
            _client.UnlinkDocumentFromConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChatMessage> SendMessageAsync(
        Guid conversationId,
        string content,
        string? model = null,
        string? odataSecret = null,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var request = new Proto.SendMessageRequest { ConversationId = conversationId.ToString(), Content = content };
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
            request.UserId = UserId.Value.ToString();
        }

        Proto.SendMessageResponse response = await RpcInvoker.InvokeAsync(
            _client.SendMessageAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Message.ToModel();
    }

    /// <inheritdoc />
    public async Task<PagedResult<ChatMessage>> ListMessagesAsync(
        Guid conversationId,
        int? pageNumber = null,
        int? pageSize = null,
        MessageRole? role = null,
        CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);
        Paging.ValidatePaging(pageNumber, pageSize);

        var request = new Proto.ListMessagesRequest
        {
            ConversationId = conversationId.ToString(),
            PageNumber = pageNumber ?? 0,
            PageSize = pageSize ?? 0,
            RoleFilter = (Proto.MessageRole)(role ?? MessageRole.Unknown),
        };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
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
    public async Task<string> ExportMarkdownAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        Ids.ThrowIfEmpty(conversationId);

        var request = new Proto.ExportConversationRequest { ConversationId = conversationId.ToString() };
        if (UserId is not null)
        {
            request.UserId = UserId.Value.ToString();
        }

        Proto.ExportConversationResponse response = await RpcInvoker.InvokeAsync(
            _client.ExportConversationAsync(request, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return response.Markdown;
    }
}
