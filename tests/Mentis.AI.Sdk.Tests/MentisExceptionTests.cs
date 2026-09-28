using Grpc.Core;

namespace Mentis.AI.Sdk.Tests;

public class MentisExceptionTests
{
    [Test]
    public void FromRpcException_SplitsErrorCodeAndMessage()
    {
        var rpc = new RpcException(new Status(StatusCode.NotFound, "Document.NotFound: Document 'x' was not found."));

        MentisException ex = MentisException.FromRpcException(rpc);

        ex.StatusCode.ShouldBe(StatusCode.NotFound);
        ex.ErrorCode.ShouldBe("Document.NotFound");
        ex.Message.ShouldBe("Document 'x' was not found.");
        ex.InnerException.ShouldBeSameAs(rpc);
        ex.ValidationErrors.ShouldBeEmpty();
    }

    [Test]
    public void FromRpcException_WithoutErrorCode_KeepsWholeDetail()
    {
        var rpc = new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid token: use Bearer"));

        MentisException ex = MentisException.FromRpcException(rpc);

        ex.ErrorCode.ShouldBeNull();
        ex.Message.ShouldBe("Missing or invalid token: use Bearer");
    }

    [Test]
    public void FromRpcException_WithEmptyDetail_UsesStatusInMessage()
    {
        MentisException ex = MentisException.FromRpcException(new RpcException(new Status(StatusCode.Internal, string.Empty)));

        ex.Message.ShouldContain("Internal");
    }

    [Test]
    public void FromRpcException_RateLimit_ExposesResourceExhaustedAndCode()
    {
        var trailers = new Metadata { { "grpc-retry-pushback-ms", "60000" } };
        var rpc = new RpcException(
            new Status(StatusCode.ResourceExhausted, "RateLimit.Exceeded: Too many requests, retry in 60 s."), trailers);

        MentisException ex = MentisException.FromRpcException(rpc);

        ex.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
        ex.ErrorCode.ShouldBe("RateLimit.Exceeded");
        ex.Message.ShouldBe("Too many requests, retry in 60 s.");
        ex.ValidationErrors.ShouldBeEmpty();
    }

    [Test]
    public void FromRpcException_ReadsValidationTrailers()
    {
        var trailers = new Metadata
        {
            { "validation-error-title", "Title must not be empty." },
            { "validation-error-title", "Title is too long." },
            { "validation-error-sizeinbytes", "Size must be positive." },
            { "other-header", "ignored" },
        };
        var rpc = new RpcException(new Status(StatusCode.InvalidArgument, "Validation.Failed: Invalid request."), trailers);

        MentisException ex = MentisException.FromRpcException(rpc);

        ex.ValidationErrors.Keys.ShouldBe(["title", "sizeinbytes"], ignoreOrder: true);
        ex.ValidationErrors["title"].ShouldBe(["Title must not be empty.", "Title is too long."]);
        ex.ValidationErrors["sizeinbytes"].ShouldBe(["Size must be positive."]);
    }
}
