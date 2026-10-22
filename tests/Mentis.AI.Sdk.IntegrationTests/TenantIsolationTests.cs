namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>
/// What one tenant must never see of another's documents. Needs a second tenant (<c>MENTIS_LIMIT_API_KEY</c>, the
/// same one the token limit tests use - any other tenant would do); ignored when it is not set.
/// </summary>
/// <remarks>
/// Guards the Manager's cross-tenant fixes of 2026-10-21: a search with foreign or made-up document ids used to
/// search other tenants' documents, and the duplicate-content check on upload used to span all tenants and named
/// the other tenant's document in its error.
/// </remarks>
public class TenantIsolationTests : IntegrationTest
{
    private MentisClient _other = null!;
    private Guid _otherTenantId;

    [OneTimeSetUp]
    public void OneTimeSetUpOtherTenant()
    {
        string? apiKey = Environment.GetEnvironmentVariable("MENTIS_LIMIT_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Ignore("MENTIS_LIMIT_API_KEY not set - tenant isolation tests skipped.");
        }

        _other = CreateClient(apiKey);
        _otherTenantId = Guid.Parse(apiKey[..apiKey.IndexOf('.', StringComparison.Ordinal)]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDownOtherTenant() => _other?.Dispose();

    [Test]
    public async Task SameContentInAnotherTenant_IsAcceptedAndRevealsNothing()
    {
        const string text = "Tenant isolation: the very same file in two tenants.";
        string content = $"{text} {Guid.NewGuid()}";

        using var first = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        Document mine = await Client.Documents.UploadAsync(first, "isolation.txt", UniqueTitle(), cancellationToken: Timeout);
        TrackDocument(mine.Id);

        using var second = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        Document theirs = await _other.Documents.UploadAsync(second, "isolation.txt", UniqueTitle(), cancellationToken: Timeout);
        TrackDocument(theirs.Id, _other);

        theirs.Id.ShouldNotBe(mine.Id);
        theirs.TenantId.ShouldBe(_otherTenantId);
    }

    [Test]
    public async Task Search_WithAnotherTenantsDocumentId_FindsNothing()
    {
        Document mine = await UploadTextAsync("The zeppelin hangar is painted turquoise and smells of lavender.");
        await Client.Documents.WaitUntilProcessedAsync(mine.Id, cancellationToken: Timeout);

        // Sanity: the owner finds it.
        (await Client.Documents.SearchAsync("zeppelin hangar turquoise", [mine.Id], topK: 3, Timeout))
            .ShouldContain(r => r.DocumentId == mine.Id);

        IReadOnlyList<DocumentSearchResult> byId = await _other.Documents.SearchAsync(
            "zeppelin hangar turquoise", [mine.Id], topK: 3, Timeout);
        IReadOnlyList<DocumentSearchResult> everything = await _other.Documents.SearchAsync(
            "zeppelin hangar turquoise", topK: 20, cancellationToken: Timeout);

        byId.ShouldBeEmpty();
        everything.ShouldNotContain(r => r.DocumentId == mine.Id);
    }

    [Test]
    public async Task AnotherTenantsDocument_IsNotFoundAndCannotBeDeleted()
    {
        Document mine = await UploadTextAsync("Private to the first tenant.");

        MentisException get = await Should.ThrowAsync<MentisException>(() => _other.Documents.GetAsync(mine.Id, Timeout));
        MentisException delete = await Should.ThrowAsync<MentisException>(() => _other.Documents.DeleteAsync(mine.Id, Timeout));

        get.StatusCode.ShouldBe(MentisStatusCode.NotFound);
        delete.StatusCode.ShouldBe(MentisStatusCode.NotFound);
        (await Client.Documents.GetAsync(mine.Id, Timeout)).Id.ShouldBe(mine.Id);
    }
}
