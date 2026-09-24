# CLAUDE.md

Context for any AI session or developer working on this repository. It
records **what has been decided and why**, so decisions are not re-litigated
and mistakes are not repeated. Keep it up to date whenever a decision changes.

## What this is

`Mentis.AI.Sdk` is a lightweight, **async-only** .NET client SDK for the
**Mentis.AI Manager** (a local LLM platform with NotebookLM-like features:
document import, chunking/embeddings, retrieval-augmented chat; gRPC-only API,
multi-tenant).

The Manager's source lives locally at `../SmartAI.Manager` (the folder still
carries the old name; the product, namespaces and proto package were renamed
`SmartAI` → `Mentis.AI`). Its GitHub repo is `Mentis.AI.Manager`. Read its
`CLAUDE.md` and `docs/ARCHITECTURE.md` when you need server-side behavior
details. **Never modify the Manager from this repo.**

## Hard requirements (set by the project owner)

1. **Target framework: .NET 10** (`net10.0`) only.
2. **Async only.** Every public operation returns `Task`/`Task<T>`/
   `IAsyncEnumerable<T>`, is suffixed `Async` and accepts a trailing
   `CancellationToken cancellationToken = default`. No synchronous
   wrappers, no `.Result`/`.Wait()`/`GetAwaiter().GetResult()` anywhere.
3. **Covers every non-admin proto RPC** currently exposed by the Manager
   (see coverage table below). `admin.proto` / `AdminService` is
   **deliberately out of scope** - do not add it.
4. **Lightweight and easy to understand.** Minimal dependencies, flat and
   predictable API surface, no clever abstractions.
5. **All documentation is in English** - README, XML doc comments, code
   comments, commit messages, this file.

## Tech stack

- .NET 10 SDK (`global.json` pins `10.0.100`, `rollForward: latestFeature`)
- `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools` (build-time only)
- `Microsoft.Extensions.DependencyInjection.Abstractions` +
  `Microsoft.Extensions.Options` only for the optional `AddMentisClient()`
  registration helper - nothing else.
- Tests: **NUnit + NSubstitute + Shouldly** (same as the Manager).
  **Not** FluentAssertions or MediatR (license changes - the Manager
  explicitly avoids them, so do we).
- Central package management via `Directory.Packages.props`.

## Repository layout

```
Mentis.AI.Sdk.slnx
Directory.Build.props            shared build settings (net10.0, nullable, analyzers)
Directory.Packages.props         central package versions
global.json
.editorconfig                    code style, copied from the Manager
src/Mentis.AI.Sdk/
  Protos/                        copies of the Manager's non-admin .proto files
  MentisClient.cs                entry point: .Documents / .Conversations / .Billing
  MentisClientOptions.cs
  Documents/                     DocumentsClient + public models
  Conversations/                 ConversationsClient + public models
  Billing/                       BillingClient + public models
  Errors/                        MentisException + RpcException translation
  Internal/                      ProtoMapper, MentisCallInterceptor, RpcInvoker, Paging
  DependencyInjection/           AddMentisClient() extension
tests/Mentis.AI.Sdk.Tests/       unit tests (mocked gRPC clients)
samples/Mentis.AI.Sdk.Sample/    minimal console app (planned)
```

Build settings: only `Mentis.AI.Sdk` is packable (`IsPackable` defaults to
false in `Directory.Build.props`). Warnings are errors in Release. The
generated gRPC clients are internal and exposed to the test project (and to
`DynamicProxyGenAssembly2` for NSubstitute) via `InternalsVisibleTo`.

## Key design decisions

### Generated proto types stay internal

Protos are compiled with `GrpcServices="Client"` and `Access="Internal"`.
The public API exposes **SDK-owned immutable `record` types and enums**,
mapped in `Internal/`. Reasons:

- `google.protobuf.Timestamp` → `DateTimeOffset`, proto `optional` →
  nullable (`string?`, `DateTimeOffset?`), `repeated` → `IReadOnlyList<T>`,
  `bytes` → `ReadOnlyMemory<byte>` / `Stream`.
- Proto enum names (`DOCUMENT_STATUS_READY`) become idiomatic
  (`DocumentStatus.Ready`). Public enum values equal the proto values, so
  mapping is a cast. `*_UNSPECIFIED = 0` becomes `Unknown = 0`, which is
  also what any value unknown to this SDK version maps to (forward
  compatibility with newer Managers). Filters are nullable parameters
  (`DocumentStatus? status = null`), never "pass Unknown for no filter".
- Consumers never need to reference Google.Protobuf types, and proto
  changes do not ripple straight into their code.

### Proto files are copied, not linked

`src/Mentis.AI.Sdk/Protos/` holds **copies** of
`../SmartAI.Manager/src/Mentis.AI.Contracts/Protos/{document,conversation,billing}.proto`.
The SDK must build standalone (CI, other machines), so no relative link into
the Manager tree. When the Manager's protos change:

1. Copy the three files over (never `admin.proto`).
2. Build - the compiler will point at every mapping that needs updating.
3. Update models, clients, the coverage table below and the README.

Keep `package mentis.ai.v1;` unchanged - it defines the wire service names
(`mentis.ai.v1.DocumentService`, ...). Changing `csharp_namespace` in the
copies is allowed (use `Mentis.AI.Sdk.Internal.Grpc`) since it only affects
generated C#.

### Authentication

- Every Manager RPC requires `Authorization: Bearer <principal>.<secret>`,
  where `<principal>` is a tenant GUID (or the literal `admin`, which this
  SDK does not target).
- Options take `TenantId` + `Secret` (or a pre-built `ApiKey` string
  `<tenantId>.<secret>`). The header is attached by a small client
  interceptor, **not** by `CallCredentials` - `CallCredentials` are
  silently dropped on plaintext channels unless
  `UnsafeUseInsecureChannelCallCredentials` is set, and the Manager is
  frequently reached over plaintext h2c locally.
- Never log or include the secret in exception messages / `ToString()`.

### Transport

- The Manager's Kestrel port (default `8080`) is **h2c only** (HTTP/2
  without TLS). `http://` endpoints work with `GrpcChannel` out of the box on
  .NET 10; `https://` is used when a TLS-terminating reverse proxy sits in
  front.
- Uploads send the whole file in one unary message (`bytes content`).
  The client's send size is unlimited by default; the server rejects
  requests above its `GrpcHost:MaxReceiveMessageSizeBytes` (4 MB default).
  The client's receive limit is 4 MB by default (matters for
  `GetContentAsync`). `MentisClientOptions` exposes
  `MaxSendMessageSizeBytes` / `MaxReceiveMessageSizeBytes`.
- The owned channel is created with `ThrowOperationCanceledOnCancellation`,
  and `RpcInvoker` additionally maps `Cancelled` + cancelled token to
  `OperationCanceledException` for externally supplied channels.

### One client, one channel

`MentisClient` owns a single `GrpcChannel` (thread-safe, meant to be reused)
and is `IAsyncDisposable`/`IDisposable`. Register it as a **singleton**;
never create one per request. A constructor overload accepts an external
`GrpcChannel`/`HttpClient` for tests and advanced scenarios (then the SDK
does not dispose it).

### End-user scoping (conversations)

Most conversation RPCs take an optional `user_id` (Phase 69 in the Manager):
set → conversation owned by that end user; unset → **tenant-global**
conversation visible to all users of the tenant. The Manager never verifies
this id; it is supplied by the upstream system.

The SDK models this as a scoped view rather than a `userId` parameter on
every method:

```csharp
var shared = client.Conversations;               // tenant-global
var alice  = client.Conversations.ForUser("alice"); // scoped to end user
```

`ForUser` returns a lightweight new `ConversationsClient` over the same
channel.

### Paging

List RPCs return `PagedResult<T>` (`Items`, `TotalCount`, `PageNumber`,
`PageSize`). Page numbers are 1-based; `0`/unset lets the server apply its
defaults (documents/conversations: 20, chunks/messages: 50) - do not
duplicate those defaults in the SDK. Convenience `IAsyncEnumerable<T>`
enumerators (`EnumerateAsync`) walk all pages.

### Errors

The Manager maps its `ErrorType` to gRPC status codes and formats the status
detail as `"<ErrorCode>: <Message>"`:

| Manager `ErrorType` | gRPC status          |
|---------------------|----------------------|
| Validation          | `InvalidArgument`    |
| NotFound            | `NotFound`           |
| Conflict            | `FailedPrecondition` |
| Unauthorized        | `Unauthenticated`    |
| Forbidden           | `PermissionDenied`   |
| Failure/Unexpected  | `Internal`           |

Validation errors additionally carry trailers `validation-error-<field>`
(lowercase field name) → message.

The SDK translates every `RpcException` into a single public
**`MentisException`** exposing `StatusCode`, `ErrorCode` (parsed prefix, may
be null), `Message`, and `ValidationErrors`
(`IReadOnlyDictionary<string, IReadOnlyList<string>>` - a field can have
several messages). The original `RpcException` is kept
as `InnerException`. Cancellation via the caller's token surfaces as
`OperationCanceledException`, not as `MentisException`.

Note the Manager's "never reveal whether another id exists" policy: a
document/conversation of another tenant yields `NotFound`, not
`PermissionDenied`.

### Billing

Tenant tokens always see only their own usage; `tenant_id` in the request is
ignored for them and only required for admin. Since the SDK is non-admin, the
`tenantId` request field is **not** exposed.

## Proto coverage (source of truth: Manager protos)

| Service               | RPC                                | SDK method                                   |
|-----------------------|------------------------------------|----------------------------------------------|
| DocumentService       | UploadDocument                     | `Documents.UploadAsync`                      |
|                       | GetDocumentById                    | `Documents.GetAsync`                         |
|                       | GetDocumentsByIds                  | `Documents.GetManyAsync`                     |
|                       | ListDocuments                      | `Documents.ListAsync` / `EnumerateAsync`     |
|                       | RenameDocument                     | `Documents.RenameAsync`                      |
|                       | DeleteDocument                     | `Documents.DeleteAsync`                      |
|                       | DeleteDocuments                    | `Documents.DeleteManyAsync`                  |
|                       | SearchDocuments                    | `Documents.SearchAsync`                      |
|                       | GetDocumentContent                 | `Documents.GetContentAsync`                  |
|                       | GetDocumentChunks                  | `Documents.GetChunksAsync`                   |
|                       | RetryDocumentProcessing            | `Documents.RetryProcessingAsync`             |
| ConversationService   | StartConversation                  | `Conversations.StartAsync`                   |
|                       | GetConversationById                | `Conversations.GetAsync`                     |
|                       | GetConversationsByIds              | `Conversations.GetManyAsync`                 |
|                       | ListConversations                  | `Conversations.ListAsync` / `EnumerateAsync` |
|                       | GetConversationsLinkedToDocument   | `Conversations.GetLinkedToDocumentAsync`     |
|                       | RenameConversation                 | `Conversations.RenameAsync`                  |
|                       | DeleteConversation                 | `Conversations.DeleteAsync`                  |
|                       | LinkDocumentToConversation         | `Conversations.LinkDocumentAsync`            |
|                       | LinkDocumentsToConversation        | `Conversations.LinkDocumentsAsync`           |
|                       | UnlinkDocumentFromConversation     | `Conversations.UnlinkDocumentAsync`          |
|                       | SendMessage                        | `Conversations.SendMessageAsync`             |
|                       | ListMessages                       | `Conversations.ListMessagesAsync`            |
|                       | ExportConversation                 | `Conversations.ExportMarkdownAsync`          |
| BillingService        | GetTenantUsage                     | `Billing.GetUsageAsync`                      |
|                       | ListTenantUsageHistory             | `Billing.GetUsageHistoryAsync`               |
| AdminService          | *(all)*                            | **out of scope**                             |

Update this table in the same change whenever an RPC is added or renamed.

## Server behavior worth knowing

- **Uploads are processed asynchronously.** `UploadAsync` returns a
  document in `Uploaded`/`Processing`; it becomes searchable only at
  `Ready`. There is no streaming status RPC, so
  `WaitUntilProcessedAsync` polls `GetAsync` until `Ready` **or** `Failed`
  and returns the document (it does not throw on `Failed`; callers check
  `Status`). No timeout parameter - callers pass a timed token.
- **Global documents** (`TenantId == null`) are readable by every tenant but
  only modifiable by admin - rename/delete on them fails for tenant tokens.
- **Citations**: `DocumentTitle` is only populated on messages returned
  directly by `SendMessage`; it is empty when read back via
  `GetConversationById`/`ListMessages`. Document this on the model.
- **`SendMessage` model override**: `model` must be the deployment's default
  or an allow-listed model, otherwise `InvalidArgument`.
- **`odata_secret`** is an ephemeral per-call credential for the tenant's
  OData source; never stored/logged by the server - and never by the SDK.
- `StartConversation` with any unknown `initial_document_ids` fails the
  whole call with `NotFound`; nothing is created.
- Monthly token limits: `TenantUsage.MonthlyTokenLimit == null` means
  unlimited.
- Rate limiting is per caller identity on the server.
- Health endpoints (`/health`, `/health/live`, `/health/ready`,
  `/health/llm`) are plain HTTP/2 GETs, not protos - **not part of the SDK
  for now**.

## Coding conventions

- File-scoped namespaces, `Nullable` enabled, warnings as errors in Release.
- Public types get XML doc comments (`GenerateDocumentationFile=true`);
  internal types only where the "why" is non-obvious.
- Validate arguments at the public boundary with `ArgumentException.ThrowIf*`
  / `ArgumentNullException.ThrowIfNull` - fail fast before a network call.
- `ConfigureAwait(false)` on every `await` inside the library.
- Keep public types `sealed` unless there is a reason not to.
- No static/global state; everything hangs off `MentisClient`.

## Testing

- Unit tests substitute the generated gRPC clients with NSubstitute. Mock
  the overload `XxxAsync(request, Metadata, DateTime?, CancellationToken)` -
  that is the one the SDK calls. Completed calls come from
  `GrpcTestCalls.Success/Failure` (plain `AsyncUnaryCall` constructor, no
  `Grpc.Core.Testing` dependency). Service clients have internal
  constructors taking the generated client, so tests build them directly.
- Tests cover mapping, request building (incl. optional-field presence
  such as `HasUserId`), argument validation and error translation.
- Integration tests against a real Manager (skipped unless
  `MENTIS_ENDPOINT` / `MENTIS_API_KEY` are set) are planned, not yet present.

```bash
dotnet build
dotnet test
```
