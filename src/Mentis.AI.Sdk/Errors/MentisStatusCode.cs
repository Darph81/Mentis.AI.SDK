namespace Mentis.AI.Sdk;

/// <summary>
/// Outcome category of a failed call, see <see cref="MentisException.StatusCode"/>. The names and numeric values
/// are those of the gRPC status codes the Manager uses, so you do not need a reference to gRPC to handle errors.
/// </summary>
public enum MentisStatusCode
{
    /// <summary>Not an error. Never carried by a <see cref="MentisException"/>.</summary>
    Ok = 0,

    /// <summary>The operation was cancelled.</summary>
    Cancelled = 1,

    /// <summary>Unknown error.</summary>
    Unknown = 2,

    /// <summary>Validation failed - see <see cref="MentisException.ValidationErrors"/>.</summary>
    InvalidArgument = 3,

    /// <summary>The configured <see cref="MentisClientOptions.Timeout"/> expired.</summary>
    DeadlineExceeded = 4,

    /// <summary>The resource does not exist, or belongs to another tenant.</summary>
    NotFound = 5,

    /// <summary>The resource already exists.</summary>
    AlreadyExists = 6,

    /// <summary>The caller is not allowed to do this, e.g. modifying a global document.</summary>
    PermissionDenied = 7,

    /// <summary>
    /// A limit was hit. Check <see cref="MentisException.ErrorCode"/>: <c>RateLimit.Exceeded</c> means the request
    /// was not processed and can be retried after the wait given in the message; the status is also used for
    /// oversized messages.
    /// </summary>
    ResourceExhausted = 8,

    /// <summary>Conflict with the current state, e.g. duplicate document content or a used-up token limit.</summary>
    FailedPrecondition = 9,

    /// <summary>The operation was aborted.</summary>
    Aborted = 10,

    /// <summary>A value was out of range.</summary>
    OutOfRange = 11,

    /// <summary>The Manager does not implement the operation.</summary>
    Unimplemented = 12,

    /// <summary>Server-side failure, e.g. the LLM provider failed.</summary>
    Internal = 13,

    /// <summary>The Manager could not be reached.</summary>
    Unavailable = 14,

    /// <summary>Unrecoverable data loss or corruption.</summary>
    DataLoss = 15,

    /// <summary>Missing or invalid tenant credentials.</summary>
    Unauthenticated = 16,
}
