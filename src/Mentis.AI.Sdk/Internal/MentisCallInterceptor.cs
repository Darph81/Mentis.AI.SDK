using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Mentis.AI.Sdk.Internal;

/// <summary>
/// Adds the Bearer token and the default deadline to every call.
/// </summary>
/// <remarks>
/// A header interceptor is used instead of <c>CallCredentials</c> because call credentials are
/// silently dropped on plaintext (h2c) channels, which is how the Manager is usually reached locally.
/// </remarks>
internal sealed class MentisCallInterceptor : Interceptor
{
    private readonly string _authorizationHeader;
    private readonly TimeSpan? _timeout;

    public MentisCallInterceptor(string apiKey, TimeSpan? timeout)
    {
        _authorizationHeader = "Bearer " + apiKey;
        _timeout = timeout;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = new Metadata();
        if (context.Options.Headers is { } existing)
        {
            foreach (Metadata.Entry entry in existing)
            {
                headers.Add(entry);
            }
        }

        headers.Add("authorization", _authorizationHeader);

        CallOptions options = context.Options.WithHeaders(headers);
        if (_timeout is { } timeout && options.Deadline is null)
        {
            options = options.WithDeadline(DateTime.UtcNow.Add(timeout));
        }

        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options));
    }
}
