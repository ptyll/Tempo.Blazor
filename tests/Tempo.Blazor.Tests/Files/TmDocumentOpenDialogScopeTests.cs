using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Tests.Fixtures;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// Phase 3 of plan 643bba06 (decision DOCLIB-QUERY-SCOPE): the dialog states the reach of every library query with
/// <see cref="DocumentLibraryScope"/> instead of leaning on an ambiguous <c>FolderPath = null</c>; overlapping reloads
/// (tree navigation racing the search) never let an older answer overwrite a newer one; and the tree keeps exactly one
/// <c>aria-selected</c> row on the nearest VISIBLE ancestor when the active folder is hidden by a collapsed parent.
/// </summary>
public class TmDocumentOpenDialogScopeTests : LocalizationTestBase
{
    private static InMemoryDocumentLibraryProvider Seeded()
    {
        var p = new InMemoryDocumentLibraryProvider(DocumentLibraryCapabilities.All);
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs/Mobile");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Designs/Mobile/Deep");
        p.AddFolder(TempoDocumentKind.Wireframe, "/Archive");
        p.AddDocument(TempoDocumentKind.Wireframe, "Home page", "/Designs");
        p.AddDocument(TempoDocumentKind.Wireframe, "Login screen", "/Designs/Mobile");
        p.AddDocument(TempoDocumentKind.Wireframe, "Deep screen", "/Designs/Mobile/Deep");
        return p;
    }

    private IRenderedComponent<TmDocumentOpenDialog> RenderOpen(
        ITempoDocumentLibraryProvider provider,
        Action<ComponentParameterCollectionBuilder<TmDocumentOpenDialog>>? extra = null)
        => Render<TmDocumentOpenDialog>(p =>
        {
            p.Add(c => c.Provider, provider);
            p.Add(c => c.Kind, TempoDocumentKind.Wireframe);
            p.Add(c => c.Open, true);
            p.Add(c => c.SearchDebounceMs, 0);
            extra?.Invoke(p);
        });

    private static AngleSharp.Dom.IElement Node(IRenderedComponent<TmDocumentOpenDialog> cut, string text)
        => cut.FindAll(".tm-dod-tree-node").First(n => n.TextContent.Contains(text));

    private static string[] Rows(IRenderedComponent<TmDocumentOpenDialog> cut)
        => cut.FindAll(".tm-dod-row").Select(r => r.GetAttribute("data-name")!).ToArray();

    private static string[] SelectedNodes(IRenderedComponent<TmDocumentOpenDialog> cut)
        => cut.FindAll(".tm-dod-tree-node").Where(n => n.GetAttribute("aria-selected") == "true")
            .Select(n => n.TextContent.Trim()).ToArray();

    // ── Scope on the wire ───────────────────────────────────────────────────

    [Fact]
    public void Folder_Browse_UsesTheFolderScope_AndTheFolderPath()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderOpen(provider);

        Node(cut, "Designs").Click();

        cut.WaitForAssertion(() => provider.Calls.Last().FolderPath.Should().Be("/Designs"));
        provider.Calls.Last().Scope.Should().Be(DocumentLibraryScope.Folder);
        provider.Calls.First().Scope.Should().Be(DocumentLibraryScope.Folder, "the initial root browse is a folder query too");
    }

    [Fact]
    public void Recent_SendsTheLibraryScope()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderOpen(provider, p => p.Add(c => c.ShowRecent, true));

        cut.Find(".tm-dod-tree-node--recent").Click();

        cut.WaitForAssertion(() => provider.Calls.Should().Contain(c =>
            c.Scope == DocumentLibraryScope.Library && c.Search == null
            && c.SortField == DocumentLibrarySortField.Modified && c.Descending));
    }

    [Fact]
    public void Search_SendsTheLibraryScope_EvenInsideAFolder()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderOpen(provider);
        Node(cut, "Designs").Click();
        cut.WaitForAssertion(() => provider.Calls.Last().FolderPath.Should().Be("/Designs"));

        cut.Find(".tm-dod-search").Input("screen");

        cut.WaitForAssertion(() => provider.Calls.Last().Search.Should().Be("screen"));
        provider.Calls.Last().Scope.Should().Be(DocumentLibraryScope.Library);
        Rows(cut).Should().BeEquivalentTo("Login screen", "Deep screen");
    }

    [Fact]
    public void ClearingTheSearch_ReturnsToTheFolderScope()
    {
        var provider = new RecordingLibraryProvider(Seeded());
        var cut = RenderOpen(provider);
        Node(cut, "Designs").Click();
        cut.Find(".tm-dod-search").Input("screen");
        cut.WaitForAssertion(() => provider.Calls.Last().Search.Should().Be("screen"));

        cut.Find(".tm-dod-search").Input(string.Empty);

        cut.WaitForAssertion(() => provider.Calls.Last().Search.Should().BeNull());
        provider.Calls.Last().Scope.Should().Be(DocumentLibraryScope.Folder);
        provider.Calls.Last().FolderPath.Should().Be("/Designs");
    }

    // ── Overlapping reloads: the newest request wins, whatever the completion order ─────

    private sealed class QueryGatedProvider(ITempoDocumentLibraryProvider inner) : ITempoDocumentLibraryProvider
    {
        public Dictionary<string, TaskCompletionSource<DocumentLibraryPage>> Gates { get; } = new();

        public DocumentLibraryCapabilities Capabilities => inner.Capabilities;

        public static string KeyOf(string? folder, string? search) => search is { Length: > 0 } ? "search:" + search : "folder:" + folder;

        public Task<DocumentLibraryFolder> GetFolderTreeAsync(TempoDocumentKind kind, CancellationToken cancellationToken = default)
            => inner.GetFolderTreeAsync(kind, cancellationToken);

        public Task<DocumentLibraryPage> BrowseAsync(DocumentLibraryQuery query, CancellationToken cancellationToken = default)
            => Gates.TryGetValue(KeyOf(query.FolderPath, query.Search), out var gate)
                ? gate.Task
                : inner.BrowseAsync(query, cancellationToken);

        public Task<DocumentLibraryEntry?> GetEntryAsync(TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
            => inner.GetEntryAsync(kind, documentId, cancellationToken);

        public Task<DocumentLibraryFolder> CreateFolderAsync(TempoDocumentKind kind, string parentPath, string name, CancellationToken cancellationToken = default)
            => inner.CreateFolderAsync(kind, parentPath, name, cancellationToken);

        public Task RenameDocumentAsync(TempoDocumentKind kind, Guid documentId, string newName, CancellationToken cancellationToken = default)
            => inner.RenameDocumentAsync(kind, documentId, newName, cancellationToken);

        public Task RenameFolderAsync(TempoDocumentKind kind, string folderPath, string newName, CancellationToken cancellationToken = default)
            => inner.RenameFolderAsync(kind, folderPath, newName, cancellationToken);

        public Task DeleteDocumentsAsync(TempoDocumentKind kind, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
            => inner.DeleteDocumentsAsync(kind, documentIds, cancellationToken);

        public Task DeleteFolderAsync(TempoDocumentKind kind, string folderPath, CancellationToken cancellationToken = default)
            => inner.DeleteFolderAsync(kind, folderPath, cancellationToken);
    }

    private static DocumentLibraryPage PageOf(string name, int total = 1) => new()
    {
        Items = [new DocumentLibraryEntry { Id = Guid.NewGuid(), Name = name, Kind = TempoDocumentKind.Wireframe }],
        TotalCount = total
    };

    [Fact]
    public void Reload_SearchTypedAfterANavigation_WinsEvenWhenTheOlderFolderAnswerArrivesLast()
    {
        var provider = new QueryGatedProvider(Seeded());
        var cut = RenderOpen(provider);
        var folder = provider.Gates[QueryGatedProvider.KeyOf("/Archive", null)] = new TaskCompletionSource<DocumentLibraryPage>();
        var search = provider.Gates[QueryGatedProvider.KeyOf(null, "scr")] = new TaskCompletionSource<DocumentLibraryPage>();

        Node(cut, "Archive").Click();      // request 1 (slow)
        cut.Find(".tm-dod-search").Input("scr"); // request 2 (newer)

        search.SetResult(PageOf("From search"));
        cut.WaitForAssertion(() => Rows(cut).Should().Equal("From search"));

        folder.SetResult(PageOf("From the folder", total: 9));

        cut.WaitForAssertion(() => Rows(cut).Should().Equal("From search"));
        cut.FindAll(".tm-dod-load-more").Should().BeEmpty("the stale folder total (9) must not apply");
        cut.FindAll(".tm-dod-error").Should().BeEmpty();
    }

    [Fact]
    public void Reload_FolderOpenedAfterASearch_WinsEvenWhenTheOlderSearchAnswerArrivesLast()
    {
        var provider = new QueryGatedProvider(Seeded());
        var cut = RenderOpen(provider);
        var search = provider.Gates[QueryGatedProvider.KeyOf(null, "scr")] = new TaskCompletionSource<DocumentLibraryPage>();
        var folder = provider.Gates[QueryGatedProvider.KeyOf("/Archive", null)] = new TaskCompletionSource<DocumentLibraryPage>();

        cut.Find(".tm-dod-search").Input("scr"); // request 1 (slow)
        Node(cut, "Archive").Click();             // request 2 (newer): the folder click also clears the search

        folder.SetResult(PageOf("From the folder"));
        cut.WaitForAssertion(() => Rows(cut).Should().Equal("From the folder"));

        search.SetResult(PageOf("From search", total: 9));

        cut.WaitForAssertion(() => Rows(cut).Should().Equal("From the folder"));
        cut.FindAll(".tm-dod-load-more").Should().BeEmpty("the stale search total (9) must not apply");
        cut.Find(".tm-dod-search").GetAttribute("value").Should().BeNullOrEmpty("opening a folder cleared the search box");
    }

    [Fact]
    public void Reload_ThreeRapidFolderSwitches_EndOnTheLastOne_WhateverTheCompletionOrder()
    {
        var provider = new QueryGatedProvider(Seeded());
        var cut = RenderOpen(provider);
        var archive = provider.Gates[QueryGatedProvider.KeyOf("/Archive", null)] = new TaskCompletionSource<DocumentLibraryPage>();
        var designs = provider.Gates[QueryGatedProvider.KeyOf("/Designs", null)] = new TaskCompletionSource<DocumentLibraryPage>();
        var mobile = provider.Gates[QueryGatedProvider.KeyOf("/Designs/Mobile", null)] = new TaskCompletionSource<DocumentLibraryPage>();

        Node(cut, "Archive").Click();
        Node(cut, "Designs").Click();
        Node(cut, "Mobile").Click();

        designs.SetResult(PageOf("From Designs"));
        mobile.SetResult(PageOf("From Mobile"));
        archive.SetResult(PageOf("From Archive"));

        cut.WaitForAssertion(() => Rows(cut).Should().Equal("From Mobile"));
        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("Mobile");
    }

    // ── Collapsing an ancestor of the active folder ─────────────────────────

    [Fact]
    public void Tree_CollapsingAHigherAncestor_MovesAriaSelectedToTheNearestVisibleOne_NotToTheHiddenOne()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Deep").Click();
        cut.WaitForState(() => Rows(cut).Contains("Deep screen"));
        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("Deep");

        // Mobile collapses first (it hides Deep), then Designs (it hides Mobile too).
        Node(cut, "Mobile").QuerySelector(".tm-dod-tree-toggle")!.Click();
        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("Mobile", "Mobile is the nearest visible ancestor");

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();
        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("Designs", "Designs is now the nearest visible ancestor");
        cut.FindAll(".tm-dod-tree-node").Should().NotContain(n => n.TextContent.Contains("Mobile"));
    }

    [Fact]
    public void Tree_ExpandingTheCollapsedAncestor_GivesAriaSelectedBackToTheActiveFolder()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Mobile").Click();
        cut.WaitForState(() => Rows(cut).Contains("Login screen"));
        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();
        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("Designs");

        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("Mobile");
    }

    [Fact]
    public void Tree_CollapsingTheRootOfTheTree_LeavesTheRootAsTheOnlySelectedRow()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Mobile").Click();
        cut.WaitForState(() => Rows(cut).Contains("Login screen"));

        Node(cut, "All documents").QuerySelector(".tm-dod-tree-toggle")!.Click();

        cut.FindAll(".tm-dod-tree-node").Should().ContainSingle();
        SelectedNodes(cut).Should().ContainSingle().Which.Should().Contain("All documents");
    }

    [Fact]
    public void Tree_AriaSelected_IsNeverOnTwoRows_AndTheSelectedRowIsTheTabStopOfTheTree()
    {
        var cut = RenderOpen(Seeded());
        Node(cut, "Deep").Click();
        cut.WaitForState(() => Rows(cut).Contains("Deep screen"));
        Node(cut, "Designs").QuerySelector(".tm-dod-tree-toggle")!.Click();

        SelectedNodes(cut).Should().HaveCount(1);
        cut.FindAll(".tm-dod-tree-node").Where(n => n.GetAttribute("tabindex") == "0")
            .Should().ContainSingle().Which.GetAttribute("aria-selected").Should().Be("true");
    }
}
