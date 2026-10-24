# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the major version is `0`, minor versions may contain breaking changes.

## [Unreleased]

## [0.1.0] - 2026-10-24

First public release.

### Added

- `MentisClient` with `Documents`, `Conversations` and `Billing`: every non-admin
  RPC of the Mentis.AI Manager, async-only, with a `CancellationToken` on every call.
- Documents: upload (from a file or a stream), get, list with filters
  (`status`, `titleContains`, `textContains`, `scope`), paging and `EnumerateAsync`,
  rename, delete (single and bulk), semantic search (`documentIds: null` searches every
  document the tenant can read; an empty list is rejected), content download, chunks,
  retry processing and `WaitUntilProcessedAsync`.
- Conversations: start, get, list, rename, delete, link and unlink documents,
  send a message (with optional `model` override and `odataSecret` - OData is opt-in per
  message: a credential, or an empty string for a source without authentication), list messages,
  export as Markdown, and `ForUser(userId)` for conversations owned by one end user.
- MCP: `SendMessageAsync(..., mcpServer: new McpServer { ... })` lets the model call
  the tools of a remote MCP server for one message (Manager Phase 73/74).
- Billing: current usage and monthly usage history of the tenant.
- Built on the Manager's `Mentis.AI.Contracts` NuGet package for the gRPC messages and clients.
  It is an implementation detail: the SDK's public API uses its own models and its dependency
  does not expose the contract types to your code.
- `QueryScope` on answers (`ChatMessage.QueryScope`) and as a document list filter.
- `MentisException` for every server error: `StatusCode`, `ErrorCode` and per-field
  `ValidationErrors`; `StatusCode` is the SDK's own `MentisStatusCode` enum, so
  no gRPC `using` is needed. Rate limiting is reported as `ResourceExhausted` /
  `RateLimit.Exceeded`, an expired `Timeout` as `DeadlineExceeded`.
- Public interfaces (`IMentisClient`, `IDocumentsClient`, `IConversationsClient`,
  `IBillingClient`) for testing and any DI container, plus `AddMentisClient(...)`
  for `Microsoft.Extensions.DependencyInjection`.
- `MentisClientOptions`: endpoint, tenant id and secret (or a combined `ApiKey`),
  default `Timeout` and message size limits.
- Every id is a `Guid`; models are immutable records.
- Optional logging: `MentisClientOptions.LoggerFactory` (`AddMentisClient` uses the container's
  `ILoggerFactory`); the channel logs its calls under `Grpc.Net.Client`, never the credential.
- Opt-in retry of rate-limited calls (`MentisClientOptions.MaxRateLimitRetries`, default 0):
  the SDK waits for the next rate-limit window and repeats a call the Manager rejected with
  `RateLimit.Exceeded`; safe for every operation because the call was not processed.
- HTTP/2 keep-alive pings while a call is active (`MentisClientOptions.KeepAlivePingDelay`,
  default 30 s), so long chat calls survive idle-killing firewalls and load balancers.
- Source Link and symbol package (`.snupkg`) for debugging into the SDK.

[Unreleased]: https://github.com/Darph81/Mentis.AI.SDK/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Darph81/Mentis.AI.SDK/releases/tag/v0.1.0
