namespace Mentis.AI.Sdk.IntegrationTests;

/// <summary>The <c>ListDocuments</c> filters: text search over title and file name, and the scope filter.</summary>
public class ListFilterTests : IntegrationTest
{
    [Test]
    public async Task TextContains_MatchesFileName_TitleContainsDoesNot()
    {
        string marker = $"filemarker{Guid.NewGuid():N}";
        Document document = await UploadTextAsync("Text search test.", fileName: $"{marker}.txt");

        PagedResult<Document> byText = await Client.Documents.ListAsync(textContains: marker, cancellationToken: Timeout);
        PagedResult<Document> byTitle = await Client.Documents.ListAsync(titleContains: marker, cancellationToken: Timeout);

        byText.Items.Single().Id.ShouldBe(document.Id);
        byTitle.Items.ShouldBeEmpty();
    }

    [Test]
    public async Task TextContains_MatchesTitle()
    {
        Document document = await UploadTextAsync("Title search test.");

        PagedResult<Document> page = await Client.Documents.ListAsync(textContains: document.Title, cancellationToken: Timeout);

        page.Items.Single().Id.ShouldBe(document.Id);
    }

    [Test]
    public async Task Scope_SeparatesOwnAndGlobalDocuments()
    {
        Document own = await UploadTextAsync("Scope test.");

        List<Document> tenant = await ToListAsync(Client.Documents.EnumerateAsync(scope: QueryScope.Tenant, cancellationToken: Timeout));
        List<Document> global = await ToListAsync(Client.Documents.EnumerateAsync(scope: QueryScope.Global, cancellationToken: Timeout));
        List<Document> all = await ToListAsync(Client.Documents.EnumerateAsync(cancellationToken: Timeout));

        tenant.ShouldContain(d => d.Id == own.Id);
        tenant.ShouldAllBe(d => d.TenantId == TenantId);
        global.ShouldNotContain(d => d.Id == own.Id);
        global.ShouldAllBe(d => d.IsGlobal);
        all.Count.ShouldBe(tenant.Count + global.Count);
    }

    [Test]
    public async Task Enumerate_WithSmallPages_ReturnsEveryDocumentOnce()
    {
        string marker = UniqueTitle();
        Document[] uploaded = [
            await UploadTextAsync("Paging 1.", $"{marker}-1"),
            await UploadTextAsync("Paging 2.", $"{marker}-2"),
            await UploadTextAsync("Paging 3.", $"{marker}-3"),
        ];

        List<Document> found = await ToListAsync(
            Client.Documents.EnumerateAsync(titleContains: marker, pageSize: 2, cancellationToken: Timeout));

        found.Select(d => d.Id).ShouldBe(uploaded.Select(d => d.Id), ignoreOrder: true);
    }

    private static async Task<List<Document>> ToListAsync(IAsyncEnumerable<Document> documents)
    {
        var list = new List<Document>();
        await foreach (Document document in documents)
        {
            list.Add(document);
        }

        return list;
    }
}
