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
