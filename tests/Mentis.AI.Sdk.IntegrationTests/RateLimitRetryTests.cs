namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Exercises <see cref="MentisClientOptions.MaxRateLimitRetries"/> against the real Manager's rate limiter
/// (fixed window of 100 requests per tenant, 60 s by default). Uses the token-limit tenant
/// (<c>MENTIS_LIMIT_API_KEY</c>) so the main test tenant's window is not consumed. Takes up to a few minutes,
/// hence explicit: run with <c>--filter "TestCategory=RateLimit"</c>.
/// </summary>
[Explicit("Takes minutes: deliberately exceeds the Manager's rate limit.")]
[Category("RateLimit")]
public class RateLimitRetryTests : IntegrationTest
{
    [Test]
    public async Task ExceedingTheRateLimit_IsRetriedTransparently()
    {
        string? apiKey = Environment.GetEnvironmentVariable("MENTIS_LIMIT_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Ignore("MENTIS_LIMIT_API_KEY not set - rate limit retry test skipped.");
        }

        // The SDK's own channel (the shared test channel has its own retry policy that would hide the feature).
        await using var client = new MentisClient(new MentisClientOptions
        {
            Endpoint = Endpoint,
            ApiKey = apiKey,
            MaxRateLimitRetries = 2,
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));

        // 250 calls cannot fit into one 100-request window, so the limiter must reject some of them; every call
        // still has to succeed because the SDK waits for the next window and repeats the rejected call.
        for (int i = 0; i < 250; i++)
        {
            await client.Billing.GetUsageAsync(cancellationToken: cts.Token);
        }
    }
}
