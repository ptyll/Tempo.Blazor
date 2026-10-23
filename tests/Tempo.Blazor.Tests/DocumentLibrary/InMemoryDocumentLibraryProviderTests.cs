using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Tests.Fixtures;

namespace Tempo.Blazor.Tests.DocumentLibrary;

/// <summary>
/// Behaviour tests for <see cref="InMemoryDocumentLibraryProvider"/>, the test-double that
/// pins the semantics expected of any <see cref="ITempoDocumentLibraryProvider"/>:
/// folder-scoped browse, search, sorting, paging, and folder/document management.
/// </summary>
public class InMemoryDocumentLibraryProviderTests
{
    private static InMemoryDocumentLibraryProvider BuildSeeded()
    {
        var provider = new InMemoryDocumentLibraryProvider(DocumentLibraryCapabilities.All);
        provider.AddFolder(TempoDocumentKind.Wireframe, "/Designs");
        provider.AddFolder(TempoDocumentKind.Wireframe, "/Designs/Mobile");
        provider.AddFolder(TempoDocumentKind.Wireframe, "/Archive");

        provider.AddDocument(TempoDocumentKind.Wireframe, "Home page", "/Designs",
            modifiedAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        provider.AddDocument(TempoDocumentKind.Wireframe, "Checkout", "/Designs",
            modifiedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        provider.AddDocument(TempoDocumentKind.Wireframe, "Login screen", "/Designs/Mobile",
            modifiedAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        provider.AddDocument(TempoDocumentKind.Wireframe, "Old draft", "/Archive",
            modifiedAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return provider;
    }

    [Fact]
    public void Capabilities_AreReported()
    {
        var provider = new InMemoryDocumentLibraryProvider(DocumentLibraryCapabilities.Search);

        provider.Capabilities.Should().Be(DocumentLibraryCapabilities.Search);
    }

    [Fact]
    public async Task GetEntry_ReturnsMetadataWithPreview_OrNullWhenMissing()
    {
        var provider = new InMemoryDocumentLibraryProvider(DocumentLibraryCapabilities.All);
        var id = provider.AddDocument(TempoDocumentKind.Wireframe, "Doc", "/", previewSvg: "<svg id=\"p\"/>");

        var entry = await provider.GetEntryAsync(TempoDocumentKind.Wireframe, id);
        entry.Should().NotBeNull();
        entry!.PreviewSvg.Should().Be("<svg id=\"p\"/>");

        (await provider.GetEntryAsync(TempoDocumentKind.Wireframe, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetFolderTree_ReturnsNestedRoot()
    {
        var provider = BuildSeeded();

        var root = await provider.GetFolderTreeAsync(TempoDocumentKind.Wireframe);

        root.Path.Should().Be("/");
        root.Children.Select(c => c.Path).Should().BeEquivalentTo(["/Designs", "/Archive"]);
        root.Children.Single(c => c.Path == "/Designs").Children.Single().Path
            .Should().Be("/Designs/Mobile");
    }

    [Fact]
    public async Task Browse_ScopesToFolder_NonRecursively()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs"
        });

        page.Items.Select(i => i.Name).Should().BeEquivalentTo(["Home page", "Checkout"]);
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Browse_SubtreeScope_IncludesDescendantFolders()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs",
            Scope = DocumentLibraryScope.Subtree
        });

        page.Items.Select(i => i.Name).Should().BeEquivalentTo(["Home page", "Checkout", "Login screen"]);
        page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Browse_LibraryScope_IgnoresFolderPath_AndReachesEveryFolder()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Archive",
            Scope = DocumentLibraryScope.Library
        });

        page.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task Browse_FolderScope_NullFolder_ListsOnlyTheRoot()
    {
        var provider = BuildSeeded();
        provider.AddDocument(TempoDocumentKind.Wireframe, "Root doc", "/");

        var page = await provider.BrowseAsync(new DocumentLibraryQuery { Kind = TempoDocumentKind.Wireframe });

        page.Items.Should().ContainSingle().Which.Name.Should().Be("Root doc");
    }

    [Fact]
    public async Task Browse_SortsByNameAscending_ByDefault()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs"
        });

        page.Items.Select(i => i.Name).Should().ContainInOrder("Checkout", "Home page");
    }

    [Fact]
    public async Task Browse_SortsByModifiedDescending_WhenRequested()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs",
            SortField = DocumentLibrarySortField.Modified,
            Descending = true
        });

        page.Items.Select(i => i.Name).Should().ContainInOrder("Home page", "Checkout");
    }

    [Fact]
    public async Task Browse_Search_MatchesNameAcrossFolders()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            Search = "screen"
        });

        page.Items.Should().ContainSingle().Which.Name.Should().Be("Login screen");
    }

    [Fact]
    public async Task Browse_Paging_LimitsAndReportsTotal()
    {
        var provider = BuildSeeded();

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs",
            Take = 1
        });

        page.Items.Should().HaveCount(1);
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task CreateFolder_AppearsInTree()
    {
        var provider = BuildSeeded();

        await provider.CreateFolderAsync(TempoDocumentKind.Wireframe, "/Designs", "Desktop");

        var root = await provider.GetFolderTreeAsync(TempoDocumentKind.Wireframe);
        root.Children.Single(c => c.Path == "/Designs").Children.Select(c => c.Path)
            .Should().Contain("/Designs/Desktop");
    }

    [Fact]
    public async Task CreateFolder_Duplicate_Throws()
    {
        var provider = BuildSeeded();

        var act = async () =>
            await provider.CreateFolderAsync(TempoDocumentKind.Wireframe, "/", "Designs");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RenameDocument_ChangesName()
    {
        var provider = BuildSeeded();
        var doc = (await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe, FolderPath = "/Designs"
        })).Items.First(i => i.Name == "Checkout");

        await provider.RenameDocumentAsync(TempoDocumentKind.Wireframe, doc.Id, "Cart");

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe, FolderPath = "/Designs"
        });
        page.Items.Select(i => i.Name).Should().Contain("Cart").And.NotContain("Checkout");
    }

    [Fact]
    public async Task RenameFolder_MovesDescendants()
    {
        var provider = BuildSeeded();

        await provider.RenameFolderAsync(TempoDocumentKind.Wireframe, "/Designs", "Mockups");

        var moved = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe, FolderPath = "/Mockups/Mobile"
        });
        moved.Items.Should().ContainSingle().Which.Name.Should().Be("Login screen");
    }

    [Fact]
    public async Task DeleteDocuments_RemovesThem()
    {
        var provider = BuildSeeded();
        var doc = (await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe, FolderPath = "/Archive"
        })).Items.Single();

        await provider.DeleteDocumentsAsync(TempoDocumentKind.Wireframe, [doc.Id]);

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe, FolderPath = "/Archive"
        });
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteFolder_RemovesFolderAndContentsRecursively()
    {
        var provider = BuildSeeded();

        await provider.DeleteFolderAsync(TempoDocumentKind.Wireframe, "/Designs");

        var root = await provider.GetFolderTreeAsync(TempoDocumentKind.Wireframe);
        root.Children.Select(c => c.Path).Should().NotContain("/Designs");

        var mobile = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe, FolderPath = "/Designs/Mobile"
        });
        mobile.Items.Should().BeEmpty();
    }
}
