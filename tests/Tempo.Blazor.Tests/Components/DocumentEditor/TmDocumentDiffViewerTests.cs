using System.Diagnostics;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 7 (DE-2.6, decision DE3): the diff is text aligned by BLOCK - one row per block, the removed side on the left,
/// the added side on the right, a changed block tinted on both - with a legend, a view switch and next / previous navigation.
/// </summary>
public sealed class TmDocumentDiffViewerTests : LocalizationTestBase
{
    [Fact]
    public void SideBySide_PlacesRemovedOnTheLeftAndAddedOnTheRight_AlignedByBlock()
    {
        var oldDocument = CreateDocument(("b1", "Intro stays"), ("b2", "Old paragraph goes away"), ("b3", "Closing stays"));
        var newDocument = CreateDocument(("b1", "Intro stays"), ("b3", "Closing stays"), ("b4", "Brand new paragraph"));

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument));

        var rows = cut.FindAll(".tm-document-diff__row");
        rows.Should().HaveCount(4, "intro, the removed block, the closing block and the added block are one row each");

        var removed = cut.Find("[data-change-kind='removed']");
        removed.QuerySelector("[data-side='old'] [data-diff-kind='removed']")!.TextContent.Should().Contain("Old paragraph goes away");
        removed.QuerySelector("[data-side='new']")!.TextContent.Trim().Should().BeEmpty("the removed block leaves its counterpart empty");
        removed.QuerySelector("[data-side='new']")!.ClassList.Should().Contain("tm-document-diff__cell--empty");

        var added = cut.Find("[data-change-kind='added']");
        added.QuerySelector("[data-side='new'] [data-diff-kind='added']")!.TextContent.Should().Contain("Brand new paragraph");
        added.QuerySelector("[data-side='old']")!.TextContent.Trim().Should().BeEmpty();

        // order: the removed block stays where it was between the two unchanged ones, the added one comes after them
        rows.Select(row => row.GetAttribute("data-change-kind")).Should().Equal("unchanged", "removed", "unchanged", "added");
    }

    [Fact]
    public void SideBySide_ChangedBlock_ShowsRemovedWordsOnlyLeftAndAddedWordsOnlyRight()
    {
        var oldDocument = CreateDocument(("b1", "Smlouva je platná"));
        var newDocument = CreateDocument(("b1", "Smlouva je dnes platná"));

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument));

        var row = cut.Find("[data-change-kind='changed']");
        row.QuerySelectorAll("[data-side='old'] [data-diff-kind='added']").Should().BeEmpty("added words never appear on the old side");
        row.QuerySelectorAll("[data-side='new'] [data-diff-kind='removed']").Should().BeEmpty("removed words never appear on the new side");
        row.QuerySelector("[data-side='new'] [data-diff-kind='added']")!.TextContent.Should().Contain("dnes");
        row.QuerySelector("[data-side='old']")!.TextContent.Should().Contain("Smlouva je").And.Contain("platná");
        row.QuerySelector("[data-side='new']")!.TextContent.Should().Contain("Smlouva je").And.Contain("platná");
    }

    [Fact]
    public void Mobile_DiffDefaultsToUnified_WithSegmentOlderNewerDiff()
    {
        var oldDocument = CreateDocument(("b1", "Smlouva je platná"), ("b2", "Odstraněný odstavec"));
        var newDocument = CreateDocument(("b1", "Smlouva je dnes platná"), ("b3", "Nový odstavec"));

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument).Add(p => p.Compact, true));

        // Unified and "Differences" on load; the three segments exist; the side-by-side selector is gone.
        cut.Find("[data-testid='document-diff-viewer']").GetAttribute("data-view-mode").Should().Be("unified");
        cut.Find("[data-testid='document-diff-viewer']").GetAttribute("data-segment").Should().Be("differences");
        cut.FindAll("[data-testid='document-diff-view-mode']").Should().BeEmpty("a phone has no side-by-side selector");
        cut.FindAll("[data-testid='document-diff-segments'] [role='radio']").Select(radio => radio.TextContent.Trim())
            .Should().Equal("Older", "Newer", "Differences");
        cut.FindAll("[data-testid='document-diff-unified']").Should().HaveCount(1);
        cut.FindAll("[data-diff-kind]").Should().NotBeEmpty("the differences segment marks the changed words");

        // Older: only what exists on the older side, plain text, no marks - the added block is gone.
        cut.FindAll("[data-testid='document-diff-segments'] [role='radio']")[0].Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-viewer']").GetAttribute("data-segment").Should().Be("older"));
        cut.FindAll("[data-diff-kind]").Should().BeEmpty();
        cut.FindAll(".tm-document-diff__row").Select(row => row.TextContent.Trim()).Should().Equal("Smlouva je platná", "Odstraněný odstavec");

        // Newer: the added block is back, the removed one is gone.
        cut.FindAll("[data-testid='document-diff-segments'] [role='radio']")[1].Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-viewer']").GetAttribute("data-segment").Should().Be("newer"));
        cut.FindAll("[data-diff-kind]").Should().BeEmpty();
        cut.FindAll(".tm-document-diff__row").Select(row => row.TextContent.Trim()).Should().Equal("Smlouva je dnes platná", "Nový odstavec");
    }

    [Fact]
    public void Mobile_ExplicitSideBySide_WinsOverTheCompactDefault()
    {
        var cut = Render<TmDocumentDiffViewer>(parameters => parameters
            .Add(p => p.OldDocument, CreateDocument(("b1", "A")))
            .Add(p => p.NewDocument, CreateDocument(("b1", "B")))
            .Add(p => p.Compact, true)
            .Add(p => p.ViewMode, DiffViewMode.SideBySide));

        cut.Find("[data-testid='document-diff-viewer']").GetAttribute("data-view-mode").Should().Be("sidebyside");
        cut.FindAll("[data-testid='document-diff-side-by-side']").Should().HaveCount(1);
    }

    [Fact]
    public void Unified_IsOneColumn_AndKeepsTheChangedWordsInTheSameLine()
    {
        var oldDocument = CreateDocument(("b1", "Smlouva je platná"), ("b2", "Odstraněno"));
        var newDocument = CreateDocument(("b1", "Smlouva je dnes platná"));

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument).Add(p => p.ViewMode, DiffViewMode.Unified));

        cut.FindAll("[data-testid='document-diff-unified']").Should().HaveCount(1);
        cut.FindAll("[data-testid='document-diff-side-by-side']").Should().BeEmpty();
        foreach (var row in cut.FindAll(".tm-document-diff__row"))
        {
            row.Children.Should().HaveCount(1, "unified is one column");
        }

        cut.Find("[data-change-kind='changed']").QuerySelector("[data-diff-kind='added']")!.TextContent.Should().Contain("dnes");
        cut.Find("[data-change-kind='removed'] [data-diff-kind='removed']").TextContent.Should().Contain("Odstraněno");
    }

    [Fact]
    public void ObsoleteSideBySideFalse_IsUnified_AndTrueIsSideBySide()
    {
        var oldDocument = CreateDocument(("b1", "A"));
        var newDocument = CreateDocument(("b1", "B"));

#pragma warning disable CS0618
        var unified = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument).Add(p => p.SideBySide, false));
        var sideBySide = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument).Add(p => p.SideBySide, true));
#pragma warning restore CS0618

        unified.Find("[data-testid='document-diff-viewer']").GetAttribute("data-view-mode").Should().Be("unified");
        sideBySide.Find("[data-testid='document-diff-viewer']").GetAttribute("data-view-mode").Should().Be("sidebyside");
    }

    [Fact]
    public void ViewModeSelector_SwitchesTheLayout_AndRaisesViewModeChanged()
    {
        var oldDocument = CreateDocument(("b1", "A"));
        var newDocument = CreateDocument(("b1", "B"));
        DiffViewMode? raised = null;

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument)
                      .Add(p => p.NewDocument, newDocument)
                      .Add(p => p.ViewModeChanged, EventCallback.Factory.Create<DiffViewMode>(this, mode => raised = mode)));

        cut.Find("[data-testid='document-diff-view-mode']").Change("Unified");

        raised.Should().Be(DiffViewMode.Unified);
        cut.FindAll("[data-testid='document-diff-unified']").Should().HaveCount(1);
    }

    [Fact]
    public void Legend_HasThreeItems_AddedRemovedAndChanged()
    {
        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "A"))).Add(p => p.NewDocument, CreateDocument(("b1", "B"))));

        var items = cut.FindAll("[data-testid='document-diff-legend'] li");
        items.Select(item => item.GetAttribute("data-legend")).Should().Equal("added", "removed", "changed");
        items.Select(item => item.TextContent.Trim()).Should().Equal("Added", "Removed", "Changed");
    }

    [Fact]
    public void Navigation_MovesAriaCurrentToTheNextChange_AndUpdatesTheCounter()
    {
        var oldDocument = CreateDocument(("b1", "Same"), ("b2", "Removed one"), ("b3", "Also same"), ("b4", "Old text here"));
        var newDocument = CreateDocument(("b1", "Same"), ("b3", "Also same"), ("b4", "New text here"), ("b5", "Added one"));

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument));

        // The first change is preselected: marked and counted "1 / 3" before anything is pressed.
        cut.Find("[aria-current='true']").GetAttribute("data-change-kind").Should().Be("removed");
        cut.Find("[data-testid='document-diff-position']").TextContent.Should().Contain("1").And.Contain("3");

        cut.Find("[data-testid='document-diff-next']").Click();
        cut.Find("[aria-current='true']").GetAttribute("data-change-kind").Should().Be("changed");
        cut.Find("[data-testid='document-diff-position']").TextContent.Should().Contain("2");

        cut.Find("[data-testid='document-diff-previous']").Click();
        cut.Find("[aria-current='true']").GetAttribute("data-change-kind").Should().Be("removed");
        cut.FindAll("[aria-current='true']").Should().HaveCount(1);
    }

    [Fact]
    public void SingleChange_IsPreselected_AndNextKeepsExactlyOneCurrentRow()
    {
        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "Same"), ("b2", "Old text")))
                      .Add(p => p.NewDocument, CreateDocument(("b1", "Same"), ("b2", "New text"))));

        cut.FindAll(".tm-document-diff__row[aria-current='true']").Should().HaveCount(1);
        cut.Find("[data-testid='document-diff-next']").Click();
        cut.FindAll(".tm-document-diff__row[aria-current='true']").Should().HaveCount(1, "the only change stays the current one");
    }

    [Fact]
    public void Meta_AndTitles_AreRenderedForBothSides()
    {
        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "A")))
                      .Add(p => p.NewDocument, CreateDocument(("b1", "B")))
                      .Add(p => p.OldTitle, "Baseline")
                      .Add(p => p.NewTitle, "Current")
                      .Add(p => p.OldMeta, "Version 1.0 · 12 Apr 2024 · Jan")
                      .Add(p => p.NewMeta, "Version 1.1 · 3 May 2024 · Eva"));

        cut.Find("[data-testid='document-diff-old-head']").TextContent.Should().Contain("Baseline");
        cut.Find("[data-testid='document-diff-new-head']").TextContent.Should().Contain("Current");
        cut.Find("[data-testid='document-diff-old-meta']").TextContent.Should().Be("Version 1.0 · 12 Apr 2024 · Jan");
        cut.Find("[data-testid='document-diff-new-meta']").TextContent.Should().Be("Version 1.1 · 3 May 2024 · Eva");
    }

    [Fact]
    public void Summary_ReadsTheNumbersOfTheComparison_WithAPluralFamily()
    {
        var oldDocument = CreateDocument(("b1", "Same"), ("b2", "Gone"));
        var newDocument = CreateDocument(("b1", "Same"), ("b3", "New one"), ("b4", "New two"));

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument));
        cut.Find("[data-testid='document-diff-summary']").TextContent.Should().Be("3 changes found (2 added, 1 removed, 0 changed)");

        var single = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "Same")))
                      .Add(p => p.NewDocument, CreateDocument(("b1", "Same"), ("b2", "Extra"))));
        single.Find("[data-testid='document-diff-summary']").TextContent.Should().Be("1 change found (1 added, 0 removed, 0 changed)");
    }

    [Fact]
    public void BackButton_RendersOnlyWithAHandler_AndRaisesIt()
    {
        var oldDocument = CreateDocument(("b1", "A"));
        var newDocument = CreateDocument(("b1", "B"));
        var raised = false;

        Render<TmDocumentDiffViewer>(parameters => parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument))
            .FindAll("[data-testid='document-diff-back']").Should().BeEmpty();

        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument)
                      .Add(p => p.NewDocument, newDocument)
                      .Add(p => p.OnBack, EventCallback.Factory.Create(this, () => raised = true)));
        cut.Find("[data-testid='document-diff-back']").Click();

        raised.Should().BeTrue();
    }

    [Fact]
    public void ComparisonResultWithoutSnapshots_StillListsEveryReportedChange()
    {
        var result = new DocumentCompareResult
        {
            Changes =
            [
                new DocumentCompareBlockChange { Kind = DocumentCompareChangeKind.Added, BlockId = "n1", NewText = "From a provider" },
                new DocumentCompareBlockChange { Kind = DocumentCompareChangeKind.Removed, BlockId = "o1", OldText = "Gone for good" },
            ],
            Summary = new DocumentCompareSummary { AddedBlocks = 1, RemovedBlocks = 1 },
        };

        var cut = Render<TmDocumentDiffViewer>(parameters => parameters.Add(p => p.ComparisonResult, result));

        cut.FindAll(".tm-document-diff__row").Should().HaveCount(2);
        cut.Find("[data-change-kind='added']").TextContent.Should().Contain("From a provider");
        cut.Find("[data-change-kind='removed']").TextContent.Should().Contain("Gone for good");
    }

    [Fact]
    public void LargeDocument_RendersWithinTheBudget_AndNeverMountsACanvas()
    {
        // The demo document x10 and then some: 600 blocks per side, a change in every 25th.
        var oldBlocks = Enumerable.Range(0, 600).Select(i => ($"b{i}", $"Paragraph number {i} with a few ordinary words in it.")).ToArray();
        var newBlocks = Enumerable.Range(0, 600).Select(i => ($"b{i}", i % 25 == 0 ? $"Paragraph number {i} with a few changed words in it." : $"Paragraph number {i} with a few ordinary words in it.")).ToArray();
        var oldDocument = CreateDocument(oldBlocks);
        var newDocument = CreateDocument(newBlocks);

        var stopwatch = Stopwatch.StartNew();
        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument));
        stopwatch.Stop();

        cut.FindAll(".tm-document-diff__row").Should().HaveCount(600);
        cut.FindAll("[data-change-kind='changed']").Should().HaveCount(24);
        cut.FindAll("canvas, [data-testid='document-canvas-engine-root']").Should().BeEmpty("a diff is text - two copies of the document are never drawn");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void VersionPanel_ShowDifferences_IsDisabledWithoutBothSides_AndRaisesTheRequest()
    {
        var baseline = CreateVersion("v1", "Baseline", "Smlouva je platná", DateTimeOffset.UtcNow.AddMinutes(-5));
        var current = CreateVersion("v2", "Current", "Smlouva je dnes platná", DateTimeOffset.UtcNow);
        DocumentVersionDiffRequest? request = null;

        var cut = Render<TmDocumentVersionPanel>(parameters =>
            parameters.Add(p => p.Versions, new[] { baseline, current })
                      .Add(p => p.OnSelectVersion, EventCallback.Factory.Create<DocumentVersion>(this, _ => { }))
                      .Add(p => p.OnShowDiff, EventCallback.Factory.Create<DocumentVersionDiffRequest>(this, r => request = r)));

        cut.Find("[data-testid='document-version-show-diff']").HasAttribute("disabled").Should().BeTrue();

        FindVersionButton(cut, "document-version-diff-base", "Baseline").Click();
        cut.Find("[data-testid='document-version-show-diff']").HasAttribute("disabled").Should().BeTrue("one side is not enough");

        FindVersionButton(cut, "document-version-diff-compare", "Current").Click();
        var button = cut.Find("[data-testid='document-version-show-diff']");
        button.HasAttribute("disabled").Should().BeFalse();
        button.Click();

        request.Should().NotBeNull();
        request!.Base.Id.Should().Be("v1");
        request.Compare.Id.Should().Be("v2");
        cut.FindAll("[data-testid='document-diff-viewer']").Should().BeEmpty("the diff is a mode of the document region now, not a block of the panel");
    }

    [Fact]
    public void ViewModeWins_OverTheObsoleteAlias_WhateverTheOrder()
    {
        var oldDocument = CreateDocument(("b1", "A"));
        var newDocument = CreateDocument(("b1", "B"));

#pragma warning disable CS0618
        var aliasFirst = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument)
                      .Add(p => p.SideBySide, false).Add(p => p.ViewMode, DiffViewMode.SideBySide));
        var aliasLast = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, oldDocument).Add(p => p.NewDocument, newDocument)
                      .Add(p => p.ViewMode, DiffViewMode.Unified).Add(p => p.SideBySide, true));
#pragma warning restore CS0618

        aliasFirst.Find("[data-testid='document-diff-viewer']").GetAttribute("data-view-mode").Should().Be("sidebyside");
        aliasLast.Find("[data-testid='document-diff-viewer']").GetAttribute("data-view-mode").Should().Be("unified");
    }

    [Fact]
    public void Segments_AreInsAndDel_WithAHiddenWord_AndTheirClassesAndKinds()
    {
        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "Smlouva je platná")))
                      .Add(p => p.NewDocument, CreateDocument(("b1", "Smlouva je dnes platná"))));

        var added = cut.Find("ins.tm-document-diff__segment--added");
        added.GetAttribute("data-diff-kind").Should().Be("added");
        added.QuerySelector(".tm-document-diff__sr")!.TextContent.Trim().Should().Be("Added:");
        added.TextContent.Should().Contain("dnes");

        var removed = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "Jedna"), ("b2", "Pryč")))
                      .Add(p => p.NewDocument, CreateDocument(("b1", "Jedna"))));
        var del = removed.Find("del.tm-document-diff__segment--removed");
        del.GetAttribute("data-diff-kind").Should().Be("removed");
        del.QuerySelector(".tm-document-diff__sr")!.TextContent.Trim().Should().Be("Removed:");
    }

    [Fact]
    public void ChangedRows_NameTheirSideForTheStackedLayout_UnchangedRowsDoNot()
    {
        var cut = Render<TmDocumentDiffViewer>(parameters =>
            parameters.Add(p => p.OldDocument, CreateDocument(("b1", "Same"), ("b2", "Old text")))
                      .Add(p => p.NewDocument, CreateDocument(("b1", "Same"), ("b2", "New text")))
                      .Add(p => p.OldTitle, "Draft").Add(p => p.NewTitle, "Final"));

        cut.Find("[data-change-kind='changed'] [data-side='old'] .tm-document-diff__side-label").TextContent.Should().Be("Draft");
        cut.Find("[data-change-kind='changed'] [data-side='new'] .tm-document-diff__side-label").TextContent.Should().Be("Final");
        cut.FindAll("[data-change-kind='unchanged'] .tm-document-diff__side-label").Should().BeEmpty();
    }

    [Fact]
    public void VersionPanel_WithoutAShowDiffHandler_OffersNoShowDifferencesButton()
    {
        var cut = Render<TmDocumentVersionPanel>(parameters =>
            parameters.Add(p => p.Versions, new[] { CreateVersion("v1", "A", "x", DateTimeOffset.UtcNow) }));

        cut.FindAll("[data-testid='document-version-show-diff']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-version-diff-base']").Should().HaveCount(1);
    }

    private static AngleSharp.Dom.IElement FindVersionButton(IRenderedComponent<TmDocumentVersionPanel> cut, string testId, string label)
        => cut.FindAll($"[data-testid='{testId}']")
              .First(button => button.ParentElement?.ParentElement?.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase) == true);

    private static DocumentEditorDocument CreateDocument(params (string Id, string Text)[] blocks)
    {
        var document = DocumentEditorDocument.Empty("doc-1");
        document.Blocks = blocks
            .Select((block, index) => new DocumentBlock
            {
                Id = block.Id,
                Order = index + 1,
                Type = DocumentBlockType.Paragraph,
                Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = block.Text }] }
            })
            .ToList();
        return document;
    }

    private static DocumentVersion CreateVersion(string id, string label, string text, DateTimeOffset createdAt)
    {
        var document = CreateDocument(("b1", text));
        var snapshot = new DocumentVersionSnapshot
        {
            DocumentId = document.DocumentId,
            SchemaVersion = document.SchemaVersion,
            Json = DocumentEditorJson.Serialize(document)
        };
        snapshot.Hash = DocumentVersionHashHelper.ComputeSnapshotHash(snapshot);

        return new DocumentVersion
        {
            Id = id,
            DocumentId = document.DocumentId,
            Label = label,
            CreatedAt = createdAt,
            Snapshot = snapshot
        };
    }
}
