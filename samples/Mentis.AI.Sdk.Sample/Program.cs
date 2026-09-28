// Minimal end-to-end walkthrough of the Mentis.AI SDK:
// upload a document, wait for processing, chat about it, show token usage, clean up.
//
// Configure via environment variables:
//   MENTIS_ENDPOINT   e.g. http://localhost:8080
//   MENTIS_API_KEY    <tenantId>.<secret>

using System.Text;

using Mentis.AI.Sdk;

string? endpoint = Environment.GetEnvironmentVariable("MENTIS_ENDPOINT");
string? apiKey = Environment.GetEnvironmentVariable("MENTIS_API_KEY");
if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set MENTIS_ENDPOINT and MENTIS_API_KEY first.");
    return 1;
}

await using var client = new MentisClient(new MentisClientOptions
{
    Endpoint = new Uri(endpoint),
    ApiKey = apiKey,
});

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
CancellationToken ct = timeout.Token;

// 1. Upload a small document and wait until it is processed.
const string text = """
    Contoso Travel Policy

    Employees may book economy class for flights shorter than six hours.
    Business class is allowed for flights of six hours or longer.
    Hotel costs are reimbursed up to 150 EUR per night.
    """;

using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
Document document = await client.Documents.UploadAsync(content, "travel-policy.txt", "Contoso Travel Policy", cancellationToken: ct);
Console.WriteLine($"Uploaded '{document.Title}' ({document.Id}), status {document.Status}");

document = await client.Documents.WaitUntilProcessedAsync(document.Id, cancellationToken: ct);
Console.WriteLine($"Processed: {document.Status}, {document.ChunkCount} chunk(s)");
if (document.Status == DocumentStatus.Failed)
{
    Console.Error.WriteLine($"Processing failed: {document.FailureReason}");
    await client.Documents.DeleteAsync(document.Id, ct);
    return 1;
}

Conversation? conversation = null;
try
{
    // 2. Chat about it.
    conversation = await client.Conversations.StartAsync("Travel questions", [document.Id], ct);
    ChatMessage answer = await client.Conversations.SendMessageAsync(
        conversation.Id,
        "Up to how much is a hotel night reimbursed?",
        cancellationToken: ct);

    Console.WriteLine();
    Console.WriteLine($"Assistant: {answer.Content}");
    foreach (Citation citation in answer.Citations)
    {
        Console.WriteLine($"  [{citation.DocumentTitle}] {citation.Snippet}");
    }
}
finally
{
    // 3. Clean up - also when chatting failed.
    if (conversation is not null)
    {
        await client.Conversations.DeleteAsync(conversation.Id, CancellationToken.None);
    }

    await client.Documents.DeleteAsync(document.Id, CancellationToken.None);
}

// 4. Token usage of this tenant.
TenantUsage usage = await client.Billing.GetUsageAsync(cancellationToken: ct);
Console.WriteLine();
Console.WriteLine($"Tokens used in {usage.Year}-{usage.Month:00}: {usage.TotalTokens} " +
                  $"(limit: {(usage.MonthlyTokenLimit is { } limit ? $"{limit}" : "unlimited")})");
return 0;
