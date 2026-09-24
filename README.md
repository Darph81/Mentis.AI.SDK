# Mentis.AI SDK for .NET

A lightweight, async-only .NET 10 client for the **Mentis.AI Manager** -
a local LLM platform for document import, semantic search and
retrieval-augmented chat.

The SDK wraps the Manager's gRPC API in a small, idiomatic C# surface:
plain records instead of generated protobuf types, `DateTimeOffset`
instead of `Timestamp`, nullable values instead of proto `optional`, and a
single exception type for every server error.

> **Status:** early development - the API shown below is the target design.

## Features

- **Documents** - upload, list, search, rename, delete, read content and chunks, retry processing
- **Conversations** - start, link documents, chat with citations, list messages, export as Markdown
- **Billing** - current token usage and monthly history for your tenant
- **Async only** - every call is `async` and cancellable
- **Minimal dependencies** - `Grpc.Net.Client` and `Google.Protobuf`

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
    TenantId = "3f2c9a1e-...",
    Secret   = "your-tenant-secret",
});

// 1. Upload a document and wait until it is indexed
var document = await client.Documents.UploadAsync("handbook.pdf", title: "Employee Handbook");
document = await client.Documents.WaitUntilReadyAsync(document.Id);

// 2. Start a conversation grounded in that document
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

```csharp
builder.Services.AddMentisClient(options =>
{
    options.Endpoint = new Uri(builder.Configuration["Mentis:Endpoint"]!);
    options.TenantId = builder.Configuration["Mentis:TenantId"]!;
    options.Secret   = builder.Configuration["Mentis:Secret"]!;
});

// Inject MentisClient anywhere - it is registered as a singleton.
```

`MentisClient` holds one gRPC channel and is safe to share across threads.
Create it once and reuse it.

## Usage

### Documents

```csharp
// Upload from a file (type is inferred from the extension) or from a stream
var doc = await client.Documents.UploadAsync("notes.md");
var doc2 = await client.Documents.UploadAsync(stream, "report.docx", title: "Q3 Report");

// List with optional filters (1-based paging)
var page = await client.Documents.ListAsync(status: DocumentStatus.Ready, titleContains: "report");

// Or iterate over all pages
await foreach (var d in client.Documents.EnumerateAsync())
{
    Console.WriteLine($"{d.Title} ({d.Status})");
}

// Semantic search across all ready documents (or a subset)
var hits = await client.Documents.SearchAsync("travel expense policy", topK: 5);

// Content, chunks, maintenance
byte[] original = await client.Documents.GetContentAsync(doc.Id);
var chunks = await client.Documents.GetChunksAsync(doc.Id);
await client.Documents.RenameAsync(doc.Id, "Meeting Notes");
await client.Documents.RetryProcessingAsync(doc.Id);   // after a failed import
await client.Documents.DeleteAsync(doc.Id);
```

Uploads are processed in the background. A freshly uploaded document is in
`Uploaded` or `Processing` state and becomes searchable once it is `Ready`.

### Conversations

Conversations are either **tenant-global** (visible to every user of your
tenant) or owned by a specific **end user** of your application:

```csharp
var shared = client.Conversations;                  // tenant-global
var alice  = client.Conversations.ForUser("alice"); // only Alice's conversations

var chat = await alice.StartAsync("Onboarding");
await alice.LinkDocumentsAsync(chat.Id, [doc.Id, doc2.Id]);

var reply = await alice.SendMessageAsync(chat.Id, "Summarize the onboarding steps.");

var messages = await alice.ListMessagesAsync(chat.Id);
string markdown = await alice.ExportMarkdownAsync(chat.Id);
```

`SendMessageAsync` optionally accepts a `model` override (must be allowed by
the Manager deployment) and an `odataSecret` for tenants with a configured
OData data source.

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
try
{
    await client.Documents.GetAsync("unknown-id");
}
catch (MentisException ex) when (ex.StatusCode == StatusCode.NotFound)
{
    Console.WriteLine($"{ex.ErrorCode}: {ex.Message}");
}
catch (MentisException ex) when (ex.ValidationErrors.Count > 0)
{
    foreach (var (field, error) in ex.ValidationErrors)
        Console.WriteLine($"{field}: {error}");
}
```

| Status               | Meaning                                                    |
|----------------------|------------------------------------------------------------|
| `InvalidArgument`    | Validation failed - see `ValidationErrors`                 |
| `NotFound`           | Resource does not exist or belongs to another tenant       |
| `FailedPrecondition` | Conflict with the current state                            |
| `Unauthenticated`    | Missing or invalid tenant credentials                      |
| `PermissionDenied`   | Not allowed, e.g. modifying a global document              |
| `Internal`           | Server-side failure                                        |

Cancelling via a `CancellationToken` throws `OperationCanceledException`.

## Configuration

| Option                       | Description                                                          |
|------------------------------|----------------------------------------------------------------------|
| `Endpoint`                   | Manager address, e.g. `http://localhost:8080` or `https://ai.example.com` |
| `TenantId` / `Secret`        | Tenant credentials                                                   |
| `ApiKey`                     | Alternative to the above: `"<tenantId>.<secret>"`                    |
| `Timeout`                    | Default deadline per call (optional)                                 |
| `MaxSendMessageSizeBytes`    | Raise for uploads larger than 4 MB (the server limit must match)     |
| `MaxReceiveMessageSizeBytes` | Raise to download large documents                                    |

The Manager listens on plain HTTP/2 (h2c). Use `http://` for direct
connections and `https://` when a TLS reverse proxy is in front of it.

## Building from source

```bash
git clone <repo-url>
cd Mentis.AI.SDK
dotnet build
dotnet test
```

## License

[MIT](LICENSE)
