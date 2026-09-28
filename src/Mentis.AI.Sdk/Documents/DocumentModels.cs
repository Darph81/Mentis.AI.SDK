namespace Mentis.AI.Sdk;

/// <summary>File format of a document.</summary>
public enum DocumentType
{
    /// <summary>Not set or not known to this SDK version.</summary>
    Unknown = 0,

    /// <summary>PDF document.</summary>
    Pdf = 1,

    /// <summary>Word document (.docx).</summary>
    Docx = 2,

    /// <summary>Markdown file.</summary>
    Markdown = 3,

    /// <summary>HTML page.</summary>
    Html = 4,

    /// <summary>Plain text file.</summary>
    Txt = 5,
}

/// <summary>Processing state of a document.</summary>
public enum DocumentStatus
{
    /// <summary>Not set or not known to this SDK version.</summary>
    Unknown = 0,

    /// <summary>Stored, waiting to be processed.</summary>
    Uploaded = 1,

    /// <summary>Being extracted, chunked and embedded.</summary>
    Processing = 2,

    /// <summary>Processed and available for search and chat.</summary>
    Ready = 3,

    /// <summary>Processing failed - see <see cref="Document.FailureReason"/>.</summary>
    Failed = 4,
}

/// <summary>A document stored in the Manager.</summary>
public sealed record Document
{
    /// <summary>Document id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Original file name.</summary>
    public required string FileName { get; init; }

    /// <summary>File format.</summary>
    public required DocumentType Type { get; init; }

    /// <summary>Size of the original file in bytes.</summary>
    public required long SizeInBytes { get; init; }

    /// <summary>Processing state.</summary>
    public required DocumentStatus Status { get; init; }

    /// <summary>Why processing failed; only set when <see cref="Status"/> is <see cref="DocumentStatus.Failed"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>When the document was uploaded.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When processing finished, if it has.</summary>
    public DateTimeOffset? ProcessedAt { get; init; }

    /// <summary>Number of chunks the document was split into.</summary>
    public required int ChunkCount { get; init; }

    /// <summary>Owning tenant; <see langword="null"/> for a global document.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Whether this is a global document: readable by every tenant, but only modifiable by an administrator.
    /// </summary>
    public bool IsGlobal => TenantId is null;
}

/// <summary>A chunk of a processed document.</summary>
public sealed record DocumentChunk
{
    /// <summary>Chunk id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Position of the chunk within the document.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>Text content of the chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Whether an embedding has been computed for this chunk.</summary>
    public required bool HasEmbedding { get; init; }
}

/// <summary>A chunk matching a semantic search query.</summary>
public sealed record DocumentSearchResult
{
    /// <summary>Id of the document the chunk belongs to.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Id of the matching chunk.</summary>
    public required Guid ChunkId { get; init; }

    /// <summary>Text content of the matching chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Similarity score; higher is more relevant.</summary>
    public required double Score { get; init; }
}

/// <summary>The original file content of a document.</summary>
public sealed record DocumentContent
{
    /// <summary>Original file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Raw file bytes.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }
}
