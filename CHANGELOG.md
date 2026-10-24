# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the major version is `0`, minor versions may contain breaking changes.

## [Unreleased]

## [0.1.0] - 2026-10-24

First public release of the .NET client for the Mentis.AI Manager.

```
dotnet add package Mentis.AI.Sdk
```

Requires .NET 10, a running Mentis.AI Manager and a tenant (id and secret) issued by its
administrator. Built on `Mentis.AI.Contracts` 1.0.0. While the major version is `0`, minor
versions may contain breaking changes.

### Added

**Documents**

- Upload from a file or a stream, get, list, rename, delete (single or bulk), download the
  original file, read the chunks and retry failed processing. `WaitUntilProcessedAsync` waits
  until an upload is ready.
- Listing filters: status, title, text in title or file name (`textContains`) and whether to
  see only your own or only the global documents (`scope`). `EnumerateAsync` walks all pages.
- Semantic search over the documents the tenant can read, or over a chosen subset
  (`documentIds`). An empty list is rejected instead of silently searching everything.

**Conversations and chat**

- Start, get, list, rename and delete conversations, link and unlink documents, list the
  messages and export a conversation as Markdown.
- `SendMessageAsync` returns the answer with its citations; `ChatMessage.QueryScope` tells
  whether the Manager searched your own documents, the global ones or both.
- Optional per message: a model override, the tenant's OData data source (opt-in - pass its
  credential, or an empty string for a source without authentication) and the tools of a
  remote MCP server (`McpServer`).
- `ForUser(userId)` scopes a client to the conversations of one end user of your application.

**Billing**

- Token usage of the current or any past month, the monthly history and the tenant's limit.

**Errors and reliability**

- One exception type, `MentisException`, with `StatusCode` (the SDK's own `MentisStatusCode`,
  so no gRPC `using` is needed), the Manager's `ErrorCode` and per-field `ValidationErrors`.
  A rate-limited call is `ResourceExhausted` / `RateLimit.Exceeded`, an expired `Timeout` is
  `DeadlineExceeded`; cancelling with your token throws `OperationCanceledException`.
- Opt-in retry of rate-limited calls (`MentisClientOptions.MaxRateLimitRetries`, default 0):
  the SDK waits for the next rate-limit window and repeats the call. Safe for every operation,
  because a rejected call was not processed.
- HTTP/2 keep-alive pings while a call is running (`KeepAlivePingDelay`, default 30 s), so
  long chat calls survive idle-killing firewalls and load balancers.

**Developer experience**

- Everything is async and takes a `CancellationToken`; every id is a `Guid`; models are
  immutable records.
- Interfaces (`IMentisClient`, `IDocumentsClient`, `IConversationsClient`, `IBillingClient`)
  for testing and any DI container, plus `AddMentisClient(...)` for
  `Microsoft.Extensions.DependencyInjection`.
- `MentisClientOptions`: endpoint, tenant id and secret (or a combined `ApiKey`), default
  `Timeout`, message size limits and an optional `LoggerFactory` (the credential is never logged).
- Source Link and a symbol package (`.snupkg`) for debugging into the SDK. The gRPC contract
  (`Mentis.AI.Contracts`) is an implementation detail and not visible in your code.

[Unreleased]: https://github.com/Darph81/Mentis.AI.SDK/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Darph81/Mentis.AI.SDK/releases/tag/v0.1.0
