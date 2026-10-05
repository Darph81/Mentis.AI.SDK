using Grpc.Core;

namespace Mentis.AI.Sdk.Internal;

/// <summary>Awaits a unary call and translates gRPC errors into SDK exceptions.</summary>
internal static class RpcInvoker
{
    public static async Task<TResponse> InvokeAsync<TResponse>(
        AsyncUnaryCall<TResponse> call,
        CancellationToken cancellationToken)
    {
        using (call)
        {
            try
            {
                return await call.ResponseAsync.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Only reached on external channels created with ThrowOperationCanceledOnCancellation: the caller did
                // not cancel, so the configured Timeout (a deadline) expired.
                throw MentisException.DeadlineExceeded(ex);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ex.Status.Detail, ex, cancellationToken);
            }
            catch (RpcException ex)
            {
                throw MentisException.FromRpcException(ex);
            }
        }
    }
}
