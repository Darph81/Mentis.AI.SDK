using System.Text;

using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;

namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// Base class for tests against a real Mentis.AI Manager. Reads <c>MENTIS_ENDPOINT</c> and
/// <c>MENTIS_API_KEY</c> from the environment or from a <c>.env</c> file in the repository root;
/// all tests are ignored when they are missing.
/// </summary>
/// <remarks>
/// Use a dedicated test tenant: tests upload documents, start conversations and (category
/// <c>Llm</c>) consume tokens. Everything a test creates is deleted again in <see cref="TearDownAsync"/>.
/// </remarks>
public abstract class IntegrationTest
{
    private readonly List<Guid> _documentIds = [];
    private readonly List<(IConversationsClient Client, Guid Id)> _conversations = [];

    protected Uri Endpoint { get; private set; } = null!;

    protected string ApiKey { get; private set; } = null!;

    /// <summary>The tenant the API key belongs to (the part before the first dot).</summary>
    protected Guid TenantId => Guid.Parse(ApiKey[..ApiKey.IndexOf('.', StringComparison.Ordinal)]);

    /// <summary>
    /// Client on a channel that retries <c>ResourceExhausted</c> - the Manager's per-tenant rate limiter
    /// (100 requests/minute by default; a full test run gets close). The Manager sends
    /// <c>grpc-retry-pushback-ms</c>, so the retry waits exactly until the next window. Rate-limited calls
    /// were never processed, so retrying them is safe even for uploads and messages.
    /// </summary>
    protected MentisClient Client { get; private set; } = null!;

    private GrpcChannel? _channel;

    /// <summary>
    /// Creates a client for another tenant on the same retrying channel (e.g. a tenant with special
    /// settings). The caller disposes it; the shared channel stays open.
    /// </summary>
    protected MentisClient CreateClient(string apiKey) =>
        new(_channel!, new MentisClientOptions { ApiKey = apiKey });

    /// <summary>Cancels a test that hangs; LLM tests use a longer budget.</summary>
    protected CancellationToken Timeout { get; private set; }

    private CancellationTokenSource? _timeoutSource;

    [OneTimeSetUp]
    public void OneTimeSetUpClient()
    {
        LoadDotEnv();
        string? endpoint = Environment.GetEnvironmentVariable("MENTIS_ENDPOINT");
        string? apiKey = Environment.GetEnvironmentVariable("MENTIS_API_KEY");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Ignore("MENTIS_ENDPOINT / MENTIS_API_KEY not set - integration tests skipped.");
        }

        Endpoint = new Uri(endpoint);
        ApiKey = apiKey;
        _channel = GrpcChannel.ForAddress(Endpoint, new GrpcChannelOptions
        {
            ThrowOperationCanceledOnCancellation = true,
            ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        Names = { MethodName.Default },
                        RetryPolicy = new RetryPolicy
                        {
                            // Backoff only applies if the server sends no retry pushback.
                            MaxAttempts = 3,
                            InitialBackoff = TimeSpan.FromSeconds(10),
                            MaxBackoff = TimeSpan.FromSeconds(60),
                            BackoffMultiplier = 2,
                            RetryableStatusCodes = { StatusCode.ResourceExhausted },
                        },
                    },
                },
            },
        });
        Client = new MentisClient(_channel, new MentisClientOptions { ApiKey = ApiKey });
    }

    [OneTimeTearDown]
    public void OneTimeTearDownClient()
    {
        Client?.Dispose();
        _channel?.Dispose();
    }

    [SetUp]
    public void SetUpTimeout()
    {
        bool isLlmTest = TestContext.CurrentContext.Test.Properties["Category"].Contains("Llm");
        _timeoutSource = new CancellationTokenSource(isLlmTest ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(3));
        Timeout = _timeoutSource.Token;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        var leftovers = new List<string>();

        foreach ((IConversationsClient client, Guid id) in _conversations)
        {
            if (!await TryDeleteAsync(() => client.DeleteAsync(id)))
            {
                leftovers.Add($"conversation {id}");
            }
        }

        foreach (Guid id in _documentIds)
        {
            if (!await TryDeleteAsync(() => Client.Documents.DeleteAsync(id)))
            {
                leftovers.Add($"document {id}");
            }
        }

        _conversations.Clear();
        _documentIds.Clear();
        _timeoutSource?.Dispose();

        if (leftovers.Count > 0)
        {
            Assert.Fail($"Cleanup failed, delete manually: {string.Join(", ", leftovers)}");
        }
    }

    /// <summary>
    /// Uploads a small text document with unique content (the Manager rejects byte-identical
    /// uploads across all tenants) and registers it for cleanup.
    /// </summary>
    protected async Task<Document> UploadTextAsync(string text, string? title = null, string fileName = "sdk-integration-test.txt")
    {
        string unique = $"{text}\n\nTest run marker: {Guid.NewGuid()}";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(unique));
        Document document = await Client.Documents.UploadAsync(content, fileName, title ?? UniqueTitle(), cancellationToken: Timeout);
        _documentIds.Add(document.Id);
        return document;
    }

    protected async Task<Conversation> StartConversationAsync(
        IConversationsClient client,
        IEnumerable<Guid>? documentIds = null)
    {
        Conversation conversation = await client.StartAsync(UniqueTitle(), documentIds, Timeout);
        _conversations.Add((client, conversation.Id));
        return conversation;
    }

    protected void TrackDocument(Guid documentId) => _documentIds.Add(documentId);

    protected static string UniqueTitle() => $"sdk-it-{Guid.NewGuid():N}";

    /// <summary>
    /// Deletes one resource. <c>NotFound</c> counts as success (the test already deleted it).
    /// A rate-limit rejection that outlasts the channel's own retries is retried here once more, so a busy
    /// test run never leaves data behind.
    /// </summary>
    private static async Task<bool> TryDeleteAsync(Func<Task> delete)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await delete();
                return true;
            }
            catch (MentisException ex) when (ex.StatusCode == MentisStatusCode.NotFound)
            {
                return true;
            }
            catch (MentisException ex) when (ex.ErrorCode == "RateLimit.Exceeded" && attempt < 2)
            {
                TestContext.Out.WriteLine("Rate limited during cleanup, retrying in 60 s.");
                await Task.Delay(TimeSpan.FromSeconds(60));
            }
            catch (MentisException ex)
            {
                TestContext.Out.WriteLine($"Cleanup failed: {ex.StatusCode} {ex.Message}");
                return false;
            }
        }
    }

    private static void LoadDotEnv()
    {
        for (DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                int separator = trimmed.IndexOf('=', StringComparison.Ordinal);
                if (trimmed.StartsWith('#') || separator <= 0)
                {
                    continue;
                }

                string key = trimmed[..separator].Trim();
                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, trimmed[(separator + 1)..].Trim());
                }
            }

            return;
        }
    }
}
