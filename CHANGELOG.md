# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the major version is `0`, minor versions may contain breaking changes.

## [Unreleased]

## [0.1.0] - 2026-10-06

First public release.

### Added

- `MentisClient` with `Documents`, `Conversations` and `Billing`: every non-admin
  RPC of the Mentis.AI Manager, async-only, with a `CancellationToken` on every call.
- Documents: upload (from a file or a stream), get, list with filters
  (`status`, `titleContains`, `textContains`, `scope`), paging and `EnumerateAsync`,
  rename, delete (single and bulk), semantic search, content download, chunks,
  retry processing and `WaitUntilProcessedAsync`.
- Conversations: start, get, list, rename, delete, link and unlink documents,
  send a message (with optional `model` override and `odataSecret`), list messages,
  export as Markdown, and `ForUser(userId)` for conversations owned by one end user.
- Billing: current usage and monthly usage history of the tenant.
- `QueryScope` on answers (`ChatMessage.QueryScope`) and as a document list filter.
- `MentisException` for every server error: `StatusCode`, `ErrorCode` and per-field
  `ValidationErrors`. Rate limiting is reported as `ResourceExhausted` /
  `RateLimit.Exceeded`, an expired `Timeout` as `DeadlineExceeded`.
- Public interfaces (`IMentisClient`, `IDocumentsClient`, `IConversationsClient`,
  `IBillingClient`) for testing and any DI container, plus `AddMentisClient(...)`
  for `Microsoft.Extensions.DependencyInjection`.
- `MentisClientOptions`: endpoint, tenant id and secret (or a combined `ApiKey`),
  default `Timeout` and message size limits.
- Every id is a `Guid`; models are immutable records.
- Source Link and symbol package (`.snupkg`) for debugging into the SDK.

[Unreleased]: https://github.com/Darph81/Mentis.AI.SDK/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Darph81/Mentis.AI.SDK/releases/tag/v0.1.0
