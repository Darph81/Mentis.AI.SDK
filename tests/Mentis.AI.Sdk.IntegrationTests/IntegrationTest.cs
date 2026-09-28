using System.Text;

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

    protected MentisClient Client { get; private set; } = null!;

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
        Client = new MentisClient(new MentisClientOptions { Endpoint = Endpoint, ApiKey = ApiKey });
    }

    [OneTimeTearDown]
    public void OneTimeTearDownClient() => Client?.Dispose();

    [SetUp]
    public void SetUpTimeout()
    {
        bool isLlmTest = TestContext.CurrentContext.Test.Properties["Category"].Contains("Llm");
        _timeoutSource = new CancellationTokenSource(isLlmTest ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1));
        Timeout = _timeoutSource.Token;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        foreach ((IConversationsClient client, Guid id) in _conversations)
        {
            await IgnoreNotFoundAsync(() => client.DeleteAsync(id));
        }

        foreach (Guid id in _documentIds)
        {
            await IgnoreNotFoundAsync(() => Client.Documents.DeleteAsync(id));
        }

        _conversations.Clear();
        _documentIds.Clear();
        _timeoutSource?.Dispose();
    }

    /// <summary>
    /// Uploads a small text document with unique content (the Manager rejects byte-identical
    /// uploads across all tenants) and registers it for cleanup.
    /// </summary>
    protected async Task<Document> UploadTextAsync(string text, string? title = null)
    {
        string unique = $"{text}\n\nTest run marker: {Guid.NewGuid()}";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(unique));
        Document document = await Client.Documents.UploadAsync(content, "sdk-integration-test.txt", title ?? UniqueTitle(), cancellationToken: Timeout);
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

    private static async Task IgnoreNotFoundAsync(Func<Task> delete)
    {
        try
        {
            await delete();
        }
        catch (MentisException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            // Already deleted by the test itself.
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
