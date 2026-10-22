using FluentAssertions;
using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Tests for <see cref="DocumentLibraryStore"/> — the in-process store in Demo.Api that backs
/// both the library (metadata/browse) and the per-kind document payloads, with optimistic
/// concurrency and change publication.
/// </summary>
public sealed class DocumentLibraryStoreTests
{
    private sealed class RecordingPublisher : ITempoDocumentChangePublisher
    {
        public List<TempoDocumentChange> Changes { get; } = [];

        public Task PublishAsync(TempoDocumentChange change, CancellationToken ct = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private static DocumentLibraryStore NewStore(out RecordingPublisher publisher)
    {
        publisher = new RecordingPublisher();
        return new DocumentLibraryStore(publisher);
    }

    [Fact]
    public void CreateDocument_AssignsIdAndTimestamps()
    {
        var store = NewStore(out _);

        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{}", null);

        doc.Id.Should().NotBeEmpty();
        doc.ModifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        store.GetDocument(TempoDocumentKind.Wireframe, doc.Id).Should().NotBeNull();
    }

    [Fact]
    public void SaveDocument_BumpsModifiedAt_AndUpdatesPayload()
    {
        var store = NewStore(out _);
        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{\"a\":1}", null);
        var original = doc.ModifiedAt;

        var saved = store.SaveDocument(TempoDocumentKind.Wireframe, doc.Id, "{\"a\":2}", null);

        saved.ModifiedAt.Should().BeOnOrAfter(original);
        store.GetDocument(TempoDocumentKind.Wireframe, doc.Id)!.PayloadJson.Should().Contain("\"a\":2");
    }

    [Fact]
    public void SaveDocument_WithStaleExpectedModifiedAt_Throws()
    {
        var store = NewStore(out _);
        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{}", null);
        var stale = doc.ModifiedAt.AddMinutes(-5);

        var act = () => store.SaveDocument(TempoDocumentKind.Wireframe, doc.Id, "{}", null, stale);

        act.Should().Throw<TempoDocumentConflictException>()
            .Which.DocumentId.Should().Be(doc.Id);
    }

    [Fact]
    public void SaveDocument_WithMatchingExpectedModifiedAt_Succeeds()
    {
        var store = NewStore(out _);
        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{}", null);

        var act = () => store.SaveDocument(TempoDocumentKind.Wireframe, doc.Id, "{}", null, doc.ModifiedAt);

        act.Should().NotThrow();
    }

    [Fact]
    public void Browse_ScopesToFolder_SortsAndPages()
    {
        var store = NewStore(out _);
        store.CreateDocument(TempoDocumentKind.Wireframe, "B", "/Designs", "{}", null);
        store.CreateDocument(TempoDocumentKind.Wireframe, "A", "/Designs", "{}", null);
        store.CreateDocument(TempoDocumentKind.Wireframe, "Z", "/Other", "{}", null);

        var page = store.Browse(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs"
        });

        page.TotalCount.Should().Be(2);
        page.Items.Select(i => i.Name).Should().ContainInOrder("A", "B");
    }

    // WF4 (phase 8): the dialog's opt-in "Recent" node browses with FolderPath = null + Modified descending. The store follows
    // the DocumentLibraryQuery.FolderPath contract ("null or / lists the root"), so without a search only the ROOT is listed -
    // a provider that wants a library-wide recent list must treat null as "everywhere". Pinned so the decision
    // WF4-DOD-RECENT-NULL-FOLDER stays true.
    [Fact]
    public void Browse_NullFolderWithoutSearch_ListsOnlyTheRoot_NewestFirstWhenSortedByModified()
    {
        var store = NewStore(out _);
        store.CreateDocument(TempoDocumentKind.Wireframe, "Root old", "/", "{}", null);
        store.CreateDocument(TempoDocumentKind.Wireframe, "Nested", "/Designs", "{}", null);
        store.CreateDocument(TempoDocumentKind.Wireframe, "Root new", "/", "{}", null);

        var page = store.Browse(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = null,
            SortField = DocumentLibrarySortField.Modified,
            Descending = true,
            Take = 25
        });

        page.Items.Select(i => i.Name).Should().Equal("Root new", "Root old");
        page.TotalCount.Should().Be(2, "a document in a sub-folder is not part of a null-folder browse");
    }
    [Fact]
    public void Browse_Search_MatchesAcrossFolders()
    {
        var store = NewStore(out _);
        store.CreateDocument(TempoDocumentKind.Wireframe, "Login screen", "/a", "{}", null);
        store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/b", "{}", null);

        var page = store.Browse(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            Search = "screen"
        });

        page.Items.Should().ContainSingle().Which.Name.Should().Be("Login screen");
    }

    [Fact]
    public void CreateFolder_AppearsInTree_AndDuplicateThrows()
    {
        var store = NewStore(out _);

        store.CreateFolder(TempoDocumentKind.Wireframe, "/", "Designs");

        store.GetFolderTree(TempoDocumentKind.Wireframe).Children
            .Select(c => c.Path).Should().Contain("/Designs");

        var act = () => store.CreateFolder(TempoDocumentKind.Wireframe, "/", "Designs");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RenameFolder_MovesDescendantDocuments()
    {
        var store = NewStore(out _);
        store.CreateFolder(TempoDocumentKind.Wireframe, "/", "Designs");
        store.CreateDocument(TempoDocumentKind.Wireframe, "Doc", "/Designs", "{}", null);

        store.RenameFolder(TempoDocumentKind.Wireframe, "/Designs", "Mockups");

        store.Browse(new DocumentLibraryQuery { Kind = TempoDocumentKind.Wireframe, FolderPath = "/Mockups" })
            .Items.Should().ContainSingle().Which.Name.Should().Be("Doc");
    }

    [Fact]
    public void DeleteFolder_RemovesContentsRecursively()
    {
        var store = NewStore(out _);
        store.CreateFolder(TempoDocumentKind.Wireframe, "/", "Designs");
        store.CreateDocument(TempoDocumentKind.Wireframe, "Doc", "/Designs", "{}", null);

        store.DeleteFolder(TempoDocumentKind.Wireframe, "/Designs");

        store.Browse(new DocumentLibraryQuery { Kind = TempoDocumentKind.Wireframe, FolderPath = "/Designs" })
            .Items.Should().BeEmpty();
        store.GetFolderTree(TempoDocumentKind.Wireframe).Children.Should().NotContain(c => c.Path == "/Designs");
    }

    // ── 2.2 Change publication ────────────────────────────────────────────────

    [Fact]
    public void SaveDocument_PublishesSavedChange()
    {
        var store = NewStore(out var publisher);
        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{}", null);
        publisher.Changes.Clear();

        store.SaveDocument(TempoDocumentKind.Wireframe, doc.Id, "{}", null);

        publisher.Changes.Should().ContainSingle();
        publisher.Changes[0].ChangeType.Should().Be(TempoDocumentChangeType.Saved);
        publisher.Changes[0].DocumentId.Should().Be(doc.Id);
    }

    [Fact]
    public void RenameDocument_PublishesRenamedChange()
    {
        var store = NewStore(out var publisher);
        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{}", null);
        publisher.Changes.Clear();

        store.RenameDocument(TempoDocumentKind.Wireframe, doc.Id, "Renamed");

        publisher.Changes.Should().ContainSingle()
            .Which.ChangeType.Should().Be(TempoDocumentChangeType.Renamed);
    }

    [Fact]
    public void DeleteDocuments_PublishesDeletedChange()
    {
        var store = NewStore(out var publisher);
        var doc = store.CreateDocument(TempoDocumentKind.Wireframe, "Home", "/", "{}", null);
        publisher.Changes.Clear();

        store.DeleteDocuments(TempoDocumentKind.Wireframe, [doc.Id]);

        publisher.Changes.Should().ContainSingle()
            .Which.ChangeType.Should().Be(TempoDocumentChangeType.Deleted);
    }
}
