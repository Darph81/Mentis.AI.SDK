using Grpc.Core;

namespace Mentis.AI.Sdk;

/// <summary>
/// Raised for every error returned by the Mentis.AI Manager.
/// </summary>
/// <remarks>
/// The original <see cref="RpcException"/> is available as <see cref="Exception.InnerException"/>.
/// Cancellation through a <see cref="CancellationToken"/> surfaces as
/// <see cref="OperationCanceledException"/> instead.
/// </remarks>
public sealed class MentisException : Exception
{
    private const string ValidationErrorPrefix = "validation-error-";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValidationErrors =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Creates an exception with a default message.</summary>
    public MentisException()
        : this("The Mentis.AI Manager returned an error.")
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    public MentisException(string message)
        : this(message, innerException: null)
    {
    }

    /// <summary>Creates an exception with the given message and inner exception.</summary>
    public MentisException(string message, Exception? innerException)
        : this(MentisStatusCode.Unknown, errorCode: null, message, NoValidationErrors, innerException)
    {
    }

    private MentisException(
        MentisStatusCode statusCode,
        string? errorCode,
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>> validationErrors,
        Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ValidationErrors = validationErrors;
    }

    /// <summary>
    /// The outcome category, e.g. <see cref="MentisStatusCode.NotFound"/> or
    /// <see cref="MentisStatusCode.InvalidArgument"/>.
    /// </summary>
    public MentisStatusCode StatusCode { get; }

    /// <summary>
    /// The Manager's machine-readable error code (e.g. <c>Document.NotFound</c>), or
    /// <see langword="null"/> when the error did not carry one.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Per-field validation messages, keyed by lower-case field name. Empty unless
    /// <see cref="StatusCode"/> is <see cref="MentisStatusCode.InvalidArgument"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors { get; }

    internal static MentisException DeadlineExceeded(Exception innerException) => new(
        MentisStatusCode.DeadlineExceeded,
        errorCode: null,
        "The call did not complete within the configured Timeout.",
        NoValidationErrors,
        innerException);

    internal static MentisException FromRpcException(RpcException exception)
    {
        // The Manager formats the status detail as "<ErrorCode>: <Message>".
        string detail = exception.Status.Detail ?? string.Empty;
        string? errorCode = null;
        string message = detail;

        int separator = detail.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0 && !detail.AsSpan(0, separator).ContainsAny(' ', '\t'))
        {
            errorCode = detail[..separator];
            message = detail[(separator + 2)..];
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"The Mentis.AI Manager returned {exception.StatusCode}.";
        }

        return new MentisException(
            (MentisStatusCode)(int)exception.StatusCode,
            errorCode,
            message,
            ReadValidationErrors(exception.Trailers),
            exception);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadValidationErrors(Metadata trailers)
    {
        Dictionary<string, List<string>>? errors = null;

        foreach (Metadata.Entry entry in trailers)
        {
            if (entry.IsBinary || !entry.Key.StartsWith(ValidationErrorPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            errors ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string field = entry.Key[ValidationErrorPrefix.Length..];
            if (!errors.TryGetValue(field, out List<string>? messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.Add(entry.Value);
        }

        return errors is null
            ? NoValidationErrors
            : errors.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value, StringComparer.Ordinal);
    }
}
