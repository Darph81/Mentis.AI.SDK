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
.github/workflows/ci.yml         CI: build, unit tests, pack
scripts/protos.sh                check/sync the proto copies against the Manager
src/Mentis.AI.Sdk/
  Protos/                        copies of the Manager's non-admin .proto files
  IMentisClient.cs, MentisClient.cs   entry point: .Documents / .Conversations / .Billing
  MentisClientOptions.cs
  Documents/                     IDocumentsClient (public) + DocumentsClient (internal) + models
  Conversations/                 IConversationsClient + ConversationsClient (internal) + models
  Billing/                       IBillingClient + BillingClient (internal) + models
  Errors/                        MentisException + RpcException translation
  Internal/                      ProtoMapper, Ids, MentisCallInterceptor, RpcInvoker, Paging
  DependencyInjection/           AddMentisClient() extension
tests/Mentis.AI.Sdk.Tests/       unit tests (mocked gRPC clients)
tests/Mentis.AI.Sdk.IntegrationTests/  tests against a real Manager (see Testing)
samples/Mentis.AI.Sdk.Sample/    minimal console app: upload → chat → billing
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

### Public API is interfaces

Consumers work against `IMentisClient`, `IDocumentsClient`,
`IConversationsClient` and `IBillingClient`, so they can substitute them in
their own tests and register them in any DI container (e.g. LightInject via
`RegisterInstance`). Only `MentisClient` (the one thing you construct) is a
public class; the service client implementations are `internal sealed`.

- XML docs live on the **interfaces**; implementations use `/// <inheritdoc />`.
- Adding an RPC means adding it to the interface **and** the implementation.
- Default parameter values must be identical on interface and implementation.
- One tenant per application is the current use case. A per-tenant
  `IMentisClientFactory` (with an app-implemented credential provider) is a
  possible later addition - not built yet, don't add it speculatively.

### Proto files are copied, not linked

`src/Mentis.AI.Sdk/Protos/` holds **copies** of
`../SmartAI.Manager/src/Mentis.AI.Contracts/Protos/{document,conversation,billing}.proto`.
The SDK must build standalone (CI, other machines), so no relative link into
the Manager tree. `scripts/protos.sh` keeps the copies honest:

```bash
scripts/protos.sh check --ref origin/main   # exit 1 + diff if the copies differ
scripts/protos.sh sync  --ref origin/main   # overwrite the copies
```

- `--ref` reads the protos from that commit of the Manager repo. **Compare
  against `origin/main`**, not the Manager's working tree: without `--ref`
  the script uses whatever branch happens to be checked out there (often a
  feature branch that is ahead of what is released). Run `git fetch` in the
  Manager first.
- The only allowed difference is the `csharp_namespace` line; `admin.proto`
  is ignored. A new proto file in the Manager is reported as `MISSING`, a
  copy without counterpart as `ORPHAN`.
- The check is **local only**: both repos are private, so CI has no access
  to the Manager. Run it before every release and whenever the Manager's
  protos changed.

When the check reports a difference:

1. `scripts/protos.sh sync --ref origin/main` (never copy `admin.proto`).
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
  requests above its `GrpcHost:MaxReceiveMessageSizeBytes` - the Manager's
  shipped `appsettings.json` sets **32 MB** (verified: 5 MB uploads pass);
  gRPC's own 4 MB default only applies if a deployment removes that setting.
  The client's receive limit is 4 MB by default (matters for
  `GetContentAsync`). `MentisClientOptions` exposes
  `MaxSendMessageSizeBytes` / `MaxReceiveMessageSizeBytes`.
- **Cancellation vs. deadline** (both verified end-to-end): a cancellation by
  the caller's token surfaces as `OperationCanceledException`; an expired
  `MentisClientOptions.Timeout` surfaces as `MentisException` with
  `StatusCode.DeadlineExceeded`. The owned channel therefore must **not** set
  `GrpcChannelOptions.ThrowOperationCanceledOnCancellation` - that option
  also turns a deadline into an `OperationCanceledException` (this was a bug
  until 2026-10-05, found by the end-to-end tests). `RpcInvoker` maps
  `Cancelled` + cancelled token to `OperationCanceledException` itself, and
  - for external channels that did set the option - turns an
  `OperationCanceledException` without a cancelled caller token into
  `DeadlineExceeded`.
- gRPC reports a bare `HttpRequestException` as `Internal`; only a real
  connection failure (socket error) becomes `Unavailable`.

### One client, one channel

`MentisClient` owns a single `GrpcChannel` (thread-safe, meant to be reused)
and is `IAsyncDisposable`/`IDisposable`. Register it as a **singleton**;
never create one per request. A constructor overload accepts an external
`GrpcChannel`/`HttpClient` for tests and advanced scenarios (then the SDK
does not dispose it).

### End-user scoping (conversations)

Most conversation RPCs take an optional `user_id` (Phase 69 in the Manager):
set → conversation owned by that end user; unset → **tenant-global**
conversation visible to all users of the tenant. The id is supplied by the
upstream system and never checked against anything - but it **must be a
GUID** (the Manager answers `InvalidArgument` otherwise, verified), which is
why `ForUser` takes a `Guid`.

Visibility (verified): a user-owned conversation is visible only to that
user - not to other users and not to the tenant-global client. Tenant-global
conversations are visible to every user of the tenant.

The SDK models this as a scoped view rather than a `userId` parameter on
every method:

```csharp
var shared = client.Conversations;               // tenant-global
var alice  = client.Conversations.ForUser(aliceUserId); // scoped to end user (GUID)
```

`ForUser` returns a lightweight new `ConversationsClient` over the same
channel.

### Every id is a `Guid`

All Manager ids are GUIDs (document, chunk, conversation, message, tenant,
end user - they are `Guid`-backed value objects in the Manager's domain). The
public API therefore uses `Guid` for every id - parameters, model properties,
`IReadOnlyList<Guid>` for id lists, `Guid?` for optional ids - and never
`string`. Owner's decision: a malformed id string would only surface as the
Manager's meaningless `Internal: "An unexpected error occurred."`.

- Converted to/from the wire in one place: `Internal/Ids.cs` (argument
  checks, `ToWire`) and `ProtoMapper.ParseId`/`ParseOptionalId`.
- `Guid.Empty` is rejected at the public boundary with `ArgumentException`
  (`Ids.ThrowIfEmpty`, also for every element of an id list).
- The credential stays a string (`ApiKey` = `<tenantId>.<secret>`);
  `MentisClientOptions.TenantId` is a `Guid?`.

### Configuration is set once

Endpoint, tenant id and secret are configured **once** - via
`MentisClientOptions` or `services.AddMentisClient(endpoint, tenantId, secret)`
- and the interceptor sends them with every call. No public method takes a
tenant id or credential parameter; do not add one. `AddMentisClient`
registers `MentisClient`/`IMentisClient` plus `IDocumentsClient`,
`IConversationsClient` (tenant-global) and `IBillingClient` as singletons.
Where the credentials come from (startup config, the consuming app's
database, ...) is the consuming application's concern - the SDK only takes
the values. No static/global
configuration (e.g. a `MentisSdk.Configure()` singleton) - DI or an explicitly
created `MentisClient` only.

### Paging

List RPCs return `PagedResult<T>` (`Items`, `TotalCount`, `PageNumber`,
`PageSize`). **The Manager ignores `page_number` unless `page_size` is sent
as well** (all four list RPCs fall back to page 1 + default size) - so
`ListAsync` rejects a page number without a page size, and `EnumerateAsync`
sends the page size the server applied to page 1 explicitly for every
following page (before this fix it returned page 1 over and over, i.e.
duplicates instead of all items, once there were more than 20). Page size is
limited to 1-100 by the server's validators. Order is the Manager's (verified): documents, conversations and
messages **newest first**; chunks in document order (`SequenceNumber`);
`Conversation.Messages` (from `GetAsync`) is chronological. Manager versions
before 2026-10-04 returned a conversation's messages in database order (EF
owned collection without `OrderBy`, effectively random with GUID keys on
SQLite - verified, it flipped between runs); the Manager now sorts in the
`Conversation` aggregate itself (PR #23). The SDK **keeps its own sort** by
`CreatedAt` in `ProtoMapper`: it hides no error, costs nothing and keeps the
documented order against older Managers - unlike the rejected workaround for
the transient `NotFound`. Page numbers are 1-based; `0`/unset lets the server apply its
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

### `QueryScope` and the document list filters

`QueryScope` (`Unknown = 0`, `Global = 1`, `Tenant = 2`, `Both = 3` - the
proto values) is used twice: as `ChatMessage.QueryScope` (which documents a
`SendMessage` answer searched) and as the `scope` filter of
`Documents.ListAsync`/`EnumerateAsync` (`Tenant` = own documents only,
`Global` = global only, `null`/`Both` = all; `Unknown` is rejected). One enum
for one concept instead of two look-alikes.

- `scope: Tenant` is sent as `tenant_id` = the caller's own tenant id, which
  `MentisClient` takes from `TenantId` or the `ApiKey` principal
  (`MentisClientOptions.TenantIdOf`). The `ApiKey` is therefore validated as
  `<guid>.<secret>` when the client is created. Another tenant's id would
  only ever return an empty list (visibility runs first), so it is not
  exposed; `tenant_id` + `global_only` together are a server validation error.
- `textContains` matches title **or** file name (search boxes);
  `titleContains` only the title. All filters combine.

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
  unlimited. Once a tenant's usage reaches its limit, `SendMessage` fails
  with `FailedPrecondition` / `Tenant.MonthlyTokenLimitReached` before any
  LLM cost is spent and without storing the user's message (verified). A
  limit of `0` blocks every message and is reported as `0`, not `null`;
  everything except `SendMessage` keeps working for such a tenant. One chat call costs roughly 1.5-2k tokens
  with `llama3.2:1b`; the `Llm` integration test is ignored (not failed)
  when the test tenant's limit is used up.
- **Rate limiting** is per caller identity (tenant): a fixed window of
  `RateLimiting:PermitLimit` requests per `WindowSeconds` (Manager default
  100 / 60 s, no queueing). A rejected call gets a proper gRPC status
  (Manager fix 2026-09-28, verified): `MentisException` with
  `StatusCode.ResourceExhausted`, `ErrorCode == "RateLimit.Exceeded"`,
  message like `"Too many requests, retry in 60 s."`, plus the trailer
  `grpc-retry-pushback-ms` (wait time until the next window), sent as a
  gRPC **Trailers-Only** response (status in the single header block) - only
  that shape lets gRPC retry policies retry the call at all; a separate
  trailer block counts as a committed response (verified both ways). The request
  was **not** processed, so retrying it is safe even for non-idempotent
  calls - the basis for roadmap item 7. Note: `ResourceExhausted` also
  comes from gRPC's own message-size limits - check `ErrorCode`, not just
  the status. `Unavailable` now means "Manager unreachable" only.
  The full integration suite gets close to 100 calls, so the test client
  retries rate-limited calls (channel retry policy honoring the pushback).
- Health endpoints (`/health`, `/health/live`, `/health/ready`,
  `/health/llm`) are plain HTTP/2 GETs, not protos - **not part of the SDK
  for now**.

Verified against a running Manager (2026-09-28):

- **All ids are GUIDs** (document, conversation, user). The Manager parses
  document/conversation ids with `Guid.Parse`; a malformed id surfaces as
  `Internal: "An unexpected error occurred."` - no useful message. Hence
  `Guid` everywhere in the SDK (see "Every id is a `Guid`").
- **Duplicate content is rejected across all tenants**: uploading bytes
  identical to any existing document fails with `FailedPrecondition`
  / `Document.DuplicateContent` (message contains the existing id).
  Tests must make every upload's content unique.
- **Global documents appear in tenant listings** (`ListAsync`,
  `EnumerateAsync`, search) with `TenantId == null`. Deleting them as a
  tenant fails with `NotFound`. Never "clean up" by deleting everything a
  listing returns - only delete ids you created.
- **Fixed Manager bug - transient `NotFound` during processing**: the
  Manager's `EfDocumentRepository.UpdateAsync` and
  `EfConversationRepository.UpdateAsync` used to delete and re-insert a row
  without a transaction, so a concurrent `GetDocumentById` could see an
  existing document as `NotFound` (observed once in
  `WaitUntilProcessedAsync`). Fixed in the Manager (2026-09-28) by wrapping
  both in one transaction. The SDK deliberately has **no workaround** - if a
  transient `NotFound` shows up again, it is a Manager regression.
- **LLM errors**: when the provider fails (e.g. the Ollama model is not
  pulled), `SendMessage` returns `Internal` / `LlmProvider.Failed` with the
  provider's message.
- **`SendMessage` searches the tenant's own and/or the global documents**,
  never only the conversation's linked documents (linking is bookkeeping
  only, `GetLinkedToDocumentAsync`). Since Manager Phase 72 every question is
  classified first and the search may be narrowed to only the tenant's own
  (`QueryScope.Tenant`) or only the global documents (`QueryScope.Global`);
  `Both` searches all visible documents. The result is reported as
  `ChatMessage.QueryScope` - only on the `SendMessage` response, `Unknown`
  when read back (same "response-only" rule as `Citation.DocumentTitle`).
  Citations are the top-5 vector search hits handed to the model as context,
  not passages the answer provably used. Tests must assert "contains my
  document", never "only my document".
- Error detail format `"<Code>: <Message>"` and the `validation-error-<field>`
  trailers (e.g. `newtitle`) are confirmed. Errors raised directly in the
  gRPC layer (e.g. the `user_id` GUID check) carry no code - `ErrorCode` is
  then `null`.

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
  such as `HasUserId`), argument validation and error translation. Every
  public method of the three clients has at least one request-building test,
  and one table-driven test per client proves that `Guid.Empty` is rejected
  before any server call.
- `MentisClientEndToEndTests` run the real `MentisClient` over a real
  `GrpcChannel` with a fake `HttpMessageHandler` (and a real closed/silent
  local port): bearer header, request path, `Timeout`/deadline, cancellation,
  rate-limit and validation responses, unreachable server. Use it for
  anything involving the interceptor, options or channel behavior - mocks of
  the generated client cannot see those. Build gRPC responses with
  `GrpcResponse(...)` (length-prefixed message + `grpc-status` trailer).
- **Integration tests** (`tests/Mentis.AI.Sdk.IntegrationTests`) run against
  a real Manager. They read `MENTIS_ENDPOINT` / `MENTIS_API_KEY` from the
  environment or from the git-ignored `.env` in the repo root, and are
  ignored when those are missing. Use a dedicated test tenant.
  - Every test deletes exactly what it created (`IntegrationTest` tracks the
    ids) - never delete by listing (global documents are visible, see above).
    Cleanup survives the rate limiter, keeps going after a failed delete and
    fails the test with the leftover ids, so nothing is left behind silently.
  - The test `Client` runs on its own `GrpcChannel` (exercising the
    external-channel constructor) with a retry policy for
    `ResourceExhausted`; `grpc-retry-pushback-ms` makes it wait exactly until
    the next rate-limit window.
  - Uploads get a unique content marker (duplicate-content rule).
  - `TokenLimitTests` use a **second tenant with a monthly token limit of 0**
    (`MENTIS_LIMIT_API_KEY`, optional - ignored when missing). They verify
    the limit error deterministically and cost no tokens. The main test
    tenant (`MENTIS_API_KEY`) should have no limit, otherwise `ChatTests`
    are ignored once it is used up. `CreateClient(apiKey)` on the test base
    builds further tenant clients on the shared retrying channel.
  - Category `Llm` (`ChatTests`) calls the model, is slow and costs tokens.
    It needs the configured chat model to be available in the Manager's LLM
    provider (`ollama pull <model>` for Ollama).

```bash
dotnet build
dotnet test tests/Mentis.AI.Sdk.Tests                                   # unit tests
dotnet test tests/Mentis.AI.Sdk.IntegrationTests --filter "TestCategory!=Llm"
dotnet test tests/Mentis.AI.Sdk.IntegrationTests --filter "TestCategory=Llm"
set -a && . ./.env && set +a && dotnet run --project samples/Mentis.AI.Sdk.Sample
```

## Roadmap - open work

Work through these one step at a time, one branch/PR each. When a step is
done, tick it off here (`[x]`) in the same PR and move any lasting decisions
into the sections above. Keep the order unless the owner says otherwise.

### Required before the first release

- [x] **1. Sample app + integration tests against a real Manager.**
  - [x] Sample app and integration tests exist; h2c, auth, error format,
    documents, conversations, user scoping and billing verified (see
    "Verified against a running Manager").
  - [x] Chat (`SendMessage`, category `Llm`) verified with Ollama
    `llama3.2:1b`.
  - [x] Ids are `Guid` throughout the public API (owner's decision).
  - [x] Transient `NotFound` during processing: fixed in the Manager
    (transactional `UpdateAsync`), **no SDK workaround** (owner's decision).
- [x] **2. Proto drift check.** `scripts/protos.sh check|sync` (see "Proto
  files are copied, not linked"). Local only - CI cannot reach the private
  Manager repo.
- [x] **3. CI (GitHub Actions).** `.github/workflows/ci.yml`: build
  (Release, warnings are errors), `dotnet test` (integration tests ignore
  themselves without a Manager) and `dotnet pack` on every PR and push to
  `main`; the package is uploaded as a build artifact.
- [x] **Manager Phase 72 + list filters synced (2026-10-02).** `QueryScope`
  (`ChatMessage.QueryScope`, list `scope` filter), `textContains`, and the
  paging fix (see "Paging").
- [ ] **4. Package metadata.** `RepositoryUrl`, `PackageProjectUrl`,
  optional icon, `CHANGELOG.md`.
- [x] **5. Close test gaps** (2026-10-05). 183 unit tests (was 53): all
  client methods, `UploadAsync(filePath)`, `ConversationsClient.EnumerateAsync`,
  mapping, options validation and the `Timeout` option end-to-end - which
  uncovered the deadline bug described under "Transport".

### Worth doing - decide deliberately

- [ ] **6. HTTP/2 keep-alive pings.** `SendMessageAsync` can take minutes
  (LLM generation); reverse proxies drop idle connections. Configure
  `SocketsHttpHandler.KeepAlivePingDelay/Timeout` on the owned channel.
  Invisible to users.
- [ ] **7. Retry on transient errors.** Rate-limit rejections
  (`ResourceExhausted` / `RateLimit.Exceeded`) are safe to retry for **every**
  call (nothing was processed) and carry `grpc-retry-pushback-ms` - an opt-in
  retry for exactly these is low-risk (the integration tests already do it via
  a channel retry policy). `Unavailable` is different: the call may or may not
  have been processed, so **never** retry it for non-idempotent calls
  (`SendMessage`, `Upload*`, `Start*`, `Link*`) - that would duplicate
  messages and token costs.
- [ ] **8. Optional logging.** `ILoggerFactory` on `MentisClientOptions`,
  passed to `GrpcChannelOptions.LoggerFactory`. No new mandatory dependency
  beyond `Microsoft.Extensions.Logging.Abstractions`.

### Decided against (for now) - don't re-propose without a new reason

- Health endpoints (`/health*`) - not protos, out of scope.
- Per-tenant `IMentisClientFactory` - one tenant per application today; add
  when that changes.
- `AdminService` - out of scope by requirement.
- Binding options from `IConfiguration` - where credentials come from
  (startup, the consuming app's database, ...) is the app's concern.
- Warning on plaintext `http://` endpoints - plaintext h2c inside a Docker
  network (`http://mentis-ai:8080`) is the normal deployment.
- Static/global configuration singleton - DI or an explicit `MentisClient`.
