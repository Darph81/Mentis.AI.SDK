# Mentis.AI SDK for .NET

A lightweight, async-only .NET 10 client for the **Mentis.AI Manager** -
a local LLM platform for document import, semantic search and
retrieval-augmented chat.

The SDK wraps the Manager's gRPC API in a small, idiomatic C# surface:
plain records instead of generated protobuf types, `DateTimeOffset`
instead of `Timestamp`, nullable values instead of proto `optional`, and a
single exception type for every server error. Every id (documents,
conversations, messages, tenants, end users) is a `Guid`.

> **Status:** early development (0.x) - the API may still change.
>
> This is a client library: it needs a running Mentis.AI Manager to talk to.

## Features

- **Documents** - upload, list, search, rename, delete, read content and chunks, retry processing
- **Conversations** - start, link documents, chat with citations, list messages, export as Markdown
- **Billing** - current token usage and monthly history for your tenant
- **Async only** - every call is `async` and cancellable
- **Minimal dependencies** - `Grpc.Net.Client`, `Google.Protobuf` and the DI/Options abstractions

Administrative operations (`AdminService`) are intentionally not included.

## Requirements

- .NET 10 SDK
- A running Mentis.AI Manager (default port `8080`, HTTP/2)
- A tenant id and secret issued by your Manager administrator

## Installation

```bash
dotnet add package Mentis.AI.Sdk
```

## Quick start

```csharp
using Mentis.AI.Sdk;

await using var client = new MentisClient(new MentisClientOptions
{
    Endpoint = new Uri("http://localhost:8080"),
    TenantId = Guid.Parse("3f2c9a1e-..."),
    Secret   = "your-tenant-secret",
});

// 1. Upload a document and wait until it is processed
var document = await client.Documents.UploadAsync("handbook.pdf", title: "Employee Handbook");
document = await client.Documents.WaitUntilProcessedAsync(document.Id);
if (document.Status == DocumentStatus.Failed)
{
    throw new InvalidOperationException(document.FailureReason);
}

// 2. Start a conversation and link the document
var conversation = await client.Conversations.StartAsync(
    "Handbook questions",
    documentIds: [document.Id]);

// 3. Ask a question
var answer = await client.Conversations.SendMessageAsync(
    conversation.Id,
    "How many vacation days do I get?");

Console.WriteLine(answer.Content);
foreach (var citation in answer.Citations)
{
    Console.WriteLine($"  [{citation.DocumentTitle}] {citation.Snippet}");
}
```

## Dependency injection

Register the client once with the Manager endpoint and your tenant
credentials. They are sent with every call - no request takes a tenant id.

```csharp
builder.Services.AddMentisClient(
    endpoint: new Uri(builder.Configuration["Mentis:Endpoint"]!),
    tenantId: Guid.Parse(builder.Configuration["Mentis:TenantId"]!),
    secret:   builder.Configuration["Mentis:Secret"]!);

// Optional further settings
builder.Services.AddMentisClient(endpoint, tenantId, secret, options =>
{
    options.Timeout = TimeSpan.FromSeconds(30);
});

// Or configure everything yourself
builder.Services.AddMentisClient(options =>
{
    options.Endpoint = endpoint;
    options.ApiKey   = "<tenantId>.<secret>";
});

// Inject IMentisClient - or IDocumentsClient, IConversationsClient, IBillingClient directly.
public sealed class HandbookService(IDocumentsClient documents) { ... }
```

`MentisClient` holds one gRPC channel and is safe to share across threads.
Create it once and reuse it.

### Other DI containers (e.g. LightInject)

The SDK does not depend on a specific container. `MentisClient` performs no
I/O in its constructor, so create it once - for example after loading the
credentials from your own database at startup - and register the instances:

```csharp
var client = new MentisClient(new MentisClientOptions
{
    Endpoint = endpoint,
    TenantId = tenantId,
    Secret   = secret,
});

container.RegisterInstance<IMentisClient>(client);
container.RegisterInstance(client.Documents);      // IDocumentsClient
container.RegisterInstance(client.Conversations);  // IConversationsClient
container.RegisterInstance(client.Billing);        // IBillingClient
```

Dispose the client when the application shuts down. Containers bridged via
`Microsoft.Extensions.DependencyInjection` (such as
`LightInject.Microsoft.DependencyInjection`) can use `AddMentisClient` directly.

### Testing your code

Depend on the interfaces and substitute them in your unit tests:

```csharp
var documents = Substitute.For<IDocumentsClient>();
documents.GetAsync("d1", Arg.Any<CancellationToken>()).Returns(new Document { ... });
```

## Usage

### Documents

```csharp
// Upload from a file (type is inferred from the extension) or from a stream
var doc = await client.Documents.UploadAsync("notes.md");
var doc2 = await client.Documents.UploadAsync(stream, "report.docx", title: "Q3 Report");

// List with optional filters (newest first; pageNumber needs pageSize, max. 100 per page)
var page = await client.Documents.ListAsync(status: DocumentStatus.Ready, titleContains: "report");
var hits = await client.Documents.ListAsync(textContains: "handbook");   // title OR file name
var own  = await client.Documents.ListAsync(scope: QueryScope.Tenant);  // only your own documents
var page3 = await client.Documents.ListAsync(pageNumber: 3, pageSize: 50);

// Or iterate over all pages
await foreach (var d in client.Documents.EnumerateAsync(scope: QueryScope.Global))
{
    Console.WriteLine($"{d.Title} ({d.Status})");
}

// Semantic search across all ready documents (or a subset)
var hits = await client.Documents.SearchAsync("travel expense policy", topK: 5);

// Content, chunks, maintenance
DocumentContent original = await client.Documents.GetContentAsync(doc.Id); // FileName + bytes
var chunks = await client.Documents.GetChunksAsync(doc.Id);
await client.Documents.RenameAsync(doc.Id, "Meeting Notes");
await client.Documents.RetryProcessingAsync(doc.Id);   // after a failed import
await client.Documents.DeleteAsync(doc.Id);
```

Uploads are processed in the background. A freshly uploaded document is in
`Uploaded` or `Processing` state and becomes searchable once it is `Ready`.
`WaitUntilProcessedAsync` polls until it is `Ready` or `Failed`; pass a
timed `CancellationToken` to limit the wait.

### Conversations

Conversations are either **tenant-global** (visible to every user of your
tenant) or owned by a specific **end user** of your application:

```csharp
var shared = client.Conversations;                  // tenant-global
var alice  = client.Conversations.ForUser(aliceUserId); // only Alice's conversations (aliceUserId is a Guid)

var chat = await alice.StartAsync("Onboarding");
await alice.LinkDocumentsAsync(chat.Id, [doc.Id, doc2.Id]);

var reply = await alice.SendMessageAsync(chat.Id, "Summarize the onboarding steps.");

var messages = await alice.ListMessagesAsync(chat.Id);
string markdown = await alice.ExportMarkdownAsync(chat.Id);
```

`SendMessageAsync` optionally accepts a `model` override (must be allowed by
the Manager deployment) and an `odataSecret` for tenants with a configured
OData data source.

#### MCP tools

Pass a remote [MCP](https://modelcontextprotocol.io) server (Streamable HTTP) to
let the model call its tools while answering **one** message. The server
configuration is ephemeral - the Manager neither stores nor logs it:

```csharp
var reply = await alice.SendMessageAsync(
    chat.Id,
    "Open a ticket for the broken printer.",
    mcpServer: new McpServer
    {
        Url = new Uri("https://mcp.example.com/mcp"),
        AuthHeader = "Bearer <token>",          // optional, full Authorization value
        AllowedToolNames = ["create_ticket"],   // only these tools can be called
    });
```

The server's host must be on the Manager's admin-configured allow-list, otherwise
the call fails with `MentisException` (`PermissionDenied`, `Mcp.ServerNotAllowed`).
Other error codes: `Mcp.InsecureServerUrl`, `Mcp.InvalidServerUrl`,
`Mcp.NoAllowedToolsAvailable`, `Mcp.Timeout`, `Mcp.ToolCallLimitReached`.

Answers draw on your own documents and the global documents shared with every
tenant - linking documents to a conversation does not limit that. The Manager
classifies each question and may search only your own or only the global
documents; the answer reports this as `reply.QueryScope` (`Tenant`, `Global`
or `Both`).

### Billing

```csharp
var usage = await client.Billing.GetUsageAsync();          // current month
var march = await client.Billing.GetUsageAsync(2026, 3);
var history = await client.Billing.GetUsageHistoryAsync(); // newest first

Console.WriteLine($"{usage.TotalTokens} / {usage.MonthlyTokenLimit?.ToString() ?? "unlimited"}");
```

## Error handling

Every server error is raised as a `MentisException`:

```csharp
using Grpc.Core; // StatusCode

try
{
    await client.Documents.GetAsync(documentId);
}
catch (MentisException ex) when (ex.StatusCode == StatusCode.NotFound)
{
    Console.WriteLine($"{ex.ErrorCode}: {ex.Message}");
}
catch (MentisException ex) when (ex.ValidationErrors.Count > 0)
{
    foreach (var (field, messages) in ex.ValidationErrors)
    {
        Console.WriteLine($"{field}: {string.Join(", ", messages)}");
    }
}
```

| Status               | Meaning                                                    |
|----------------------|------------------------------------------------------------|
| `InvalidArgument`    | Validation failed - see `ValidationErrors`                 |
| `NotFound`           | Resource does not exist or belongs to another tenant       |
| `FailedPrecondition` | Conflict with the current state                            |
| `Unauthenticated`    | Missing or invalid tenant credentials                      |
| `PermissionDenied`   | Not allowed, e.g. modifying a global document              |
| `Internal`           | Server-side failure (e.g. the LLM provider failed)         |
| `ResourceExhausted`  | Rate limit hit (`ErrorCode` = `RateLimit.Exceeded`) - safe to retry after the wait in the message; also raised for oversized messages |
| `DeadlineExceeded`   | The configured `Timeout` expired                           |
| `Unavailable`        | Manager unreachable                                        |

Cancelling via a `CancellationToken` throws `OperationCanceledException`.

## Configuration

| Option                       | Description                                                          |
|------------------------------|----------------------------------------------------------------------|
| `Endpoint`                   | Manager address (default `http://localhost:8080`), or e.g. `https://ai.example.com` |
| `TenantId` / `Secret`        | Tenant credentials                                                   |
| `ApiKey`                     | Alternative to the above: `"<tenantId>.<secret>"`                    |
| `Timeout`                    | Default deadline per call (optional)                                 |
| `MaxSendMessageSizeBytes`    | Optional client-side cap on request size (default: unlimited)        |
| `MaxReceiveMessageSizeBytes` | Response size limit (default 4 MB) - raise to download large documents |

Uploads are sent as a single message. The Manager's request limit is its
`GrpcHost:MaxReceiveMessageSizeBytes` setting (32 MB in its default
configuration).

The Manager listens on plain HTTP/2 (h2c). Use `http://` for direct
connections and `https://` when a TLS reverse proxy is in front of it.

## Building from source

```bash
git clone https://github.com/Darph81/Mentis.AI.SDK.git
cd Mentis.AI.SDK
dotnet build
dotnet test tests/Mentis.AI.Sdk.Tests
```

The proto files under `src/Mentis.AI.Sdk/Protos` are copies of the Manager's.
With the Manager repository checked out next to this one, verify or update them:

```bash
scripts/protos.sh check --ref origin/main
scripts/protos.sh sync  --ref origin/main
```

### Integration tests and sample

Both run against a real Manager. Put the connection of a **dedicated test
tenant** into a `.env` file in the repository root (git-ignored):

```bash
MENTIS_ENDPOINT=http://localhost:8080
MENTIS_API_KEY=<tenantId>.<secret>
# optional: a second tenant with a monthly token limit of 0, for the token-limit tests
MENTIS_LIMIT_API_KEY=<tenantId>.<secret>
```

```bash
dotnet test tests/Mentis.AI.Sdk.IntegrationTests --filter "TestCategory!=Llm"
dotnet test tests/Mentis.AI.Sdk.IntegrationTests --filter "TestCategory=Llm"   # calls the LLM, costs tokens
set -a && . ./.env && set +a && dotnet run --project samples/Mentis.AI.Sdk.Sample
```

The tests delete everything they create. Without the variables they are skipped.

## License

[MIT](https://github.com/Darph81/Mentis.AI.SDK/blob/main/LICENSE)
