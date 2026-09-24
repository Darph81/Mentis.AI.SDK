using Grpc.Core;

namespace Mentis.AI.Sdk.Tests;

/// <summary>Builds completed <see cref="AsyncUnaryCall{TResponse}"/> instances for mocked gRPC clients.</summary>
internal static class GrpcTestCalls
{
    public static AsyncUnaryCall<T> Success<T>(T response) => Create(Task.FromResult(response));

    public static AsyncUnaryCall<T> Failure<T>(StatusCode statusCode, string detail, Metadata? trailers = null) =>
        Create(Task.FromException<T>(new RpcException(new Status(statusCode, detail), trailers ?? [])));

    private static AsyncUnaryCall<T> Create<T>(Task<T> response) =>
        new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
}
