using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Internal;

/// <summary>Maps generated proto messages to the public SDK models.</summary>
internal static class ProtoMapper
{
    public static Document ToModel(this Proto.Document document) => new()
    {
        Id = document.Id,
        Title = document.Title,
        FileName = document.FileName,
        Type = ToEnum<DocumentType>((int)document.Type),
        SizeInBytes = document.SizeInBytes,
        Status = ToEnum<DocumentStatus>((int)document.Status),
        FailureReason = document.HasFailureReason ? document.FailureReason : null,
        CreatedAt = ToDateTimeOffset(document.CreatedAt),
        ProcessedAt = document.ProcessedAt?.ToDateTimeOffset(),
        ChunkCount = document.ChunkCount,
        TenantId = document.HasTenantId ? document.TenantId : null,
    };

    public static DocumentChunk ToModel(this Proto.DocumentChunk chunk) => new()
    {
        Id = chunk.Id,
        SequenceNumber = chunk.SequenceNumber,
        Content = chunk.Content,
        HasEmbedding = chunk.HasEmbedding,
    };

    public static DocumentSearchResult ToModel(this Proto.SearchResult result) => new()
    {
        DocumentId = result.DocumentId,
        ChunkId = result.ChunkId,
        Content = result.Content,
        Score = result.Score,
    };

    public static Conversation ToModel(this Proto.Conversation conversation) => new()
    {
        Id = conversation.Id,
        Title = conversation.Title,
        CreatedAt = ToDateTimeOffset(conversation.CreatedAt),
        LinkedDocumentIds = [.. conversation.LinkedDocumentIds],
        Messages = [.. conversation.Messages.Select(ToModel)],
        TenantId = conversation.TenantId,
        OwnerUserId = EmptyToNull(conversation.HasOwnerUserId ? conversation.OwnerUserId : null),
    };

    public static ConversationSummary ToModel(this Proto.ConversationSummary summary) => new()
    {
        Id = summary.Id,
        Title = summary.Title,
        CreatedAt = ToDateTimeOffset(summary.CreatedAt),
        LinkedDocumentIds = [.. summary.LinkedDocumentIds],
        MessageCount = summary.MessageCount,
        TenantId = summary.TenantId,
        OwnerUserId = EmptyToNull(summary.HasOwnerUserId ? summary.OwnerUserId : null),
    };

    public static ChatMessage ToModel(this Proto.ChatMessage message) => new()
    {
        Id = message.Id,
        Role = ToEnum<MessageRole>((int)message.Role),
        Content = message.Content,
        CreatedAt = ToDateTimeOffset(message.CreatedAt),
        Citations = [.. message.Citations.Select(ToModel)],
    };

    public static Citation ToModel(this Proto.Citation citation) => new()
    {
        DocumentId = citation.DocumentId,
        ChunkId = citation.ChunkId,
        Snippet = citation.Snippet,
        DocumentTitle = EmptyToNull(citation.DocumentTitle),
    };

    public static TenantUsage ToModel(this Proto.TenantUsage usage) => new()
    {
        TenantId = usage.TenantId,
        Year = usage.Year,
        Month = usage.Month,
        TotalTokens = usage.TotalTokens,
        MonthlyTokenLimit = usage.HasMonthlyTokenLimit ? usage.MonthlyTokenLimit : null,
    };

    /// <summary>Values unknown to this SDK version (e.g. added by a newer Manager) become <c>Unknown</c>.</summary>
    private static TEnum ToEnum<TEnum>(int value)
        where TEnum : struct, Enum
    {
        var result = (TEnum)(object)value;
        return Enum.IsDefined(result) ? result : default;
    }

    private static DateTimeOffset ToDateTimeOffset(Timestamp? timestamp) =>
        timestamp?.ToDateTimeOffset() ?? default;

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
