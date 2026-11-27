using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Phase 2 / DE-2.1 + [A]: every panel action speaks the core button idiom (<c>tm-btn</c> + a variant), the primary
/// action of a panel is <c>tm-btn-primary</c>, and no pivot-table button class leaks into the editor markup. The
/// legacy BEM classes stay on the element as a stable alias for E2E selectors and consumer CSS.
/// </summary>
public sealed class DocumentEditorButtonsTests : LocalizationTestBase
{
    [Fact]
    public void PrimaryActions_UseTmBtnPrimary()
    {
        var version = Render<TmDocumentVersionPanel>();
        AssertPrimary(version.Find("[data-testid='document-version-create-open']"), "tm-document-version-panel__create");

        var compare = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, DocumentEditorDocument.Empty()));
        AssertPrimary(compare.Find("[data-testid='document-compare-run']"), null);

        var revisions = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { PendingRevision() })
            .Add(p => p.CanReview, true));
        AssertPrimary(revisions.Find("[data-testid='document-revision-accept-all']"), "tm-document-revision-panel__batch-action--accept");

        var find = Render<TmDocumentFindPanel>(parameters => parameters
            .Add(p => p.Document, DocumentEditorDocument.Empty())
            .Add(p => p.ShowReplace, true));
        AssertPrimary(find.Find("[data-testid='document-find-replace-one']"), "tm-document-find-panel__btn");
    }

    [Fact]
    public void SecondaryActions_UseTmBtnWithANonPrimaryVariant()
    {
        var revisions = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { PendingRevision() })
            .Add(p => p.CanReview, true));

        foreach (var testId in new[] { "document-revision-reject-all", "document-revision-accept", "document-revision-reject" })
        {
            var button = revisions.Find($"[data-testid='{testId}']");
            button.ClassList.Should().Contain("tm-btn");
            button.ClassList.Should().NotContain("tm-btn-primary", $"{testId} is not the panel's primary action");
        }

        var compare = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, DocumentEditorDocument.Empty()));
        compare.FindAll(".tm-document-version-dialog__actions button")
            .Where(button => button.GetAttribute("data-testid") != "document-compare-run")
            .Should().OnlyContain(button => button.ClassList.Contains("tm-btn") && !button.ClassList.Contains("tm-btn-primary"));
    }

    [Fact]
    public void LegacyBemClasses_StayAsAliases()
    {
        var revisions = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { PendingRevision() })
            .Add(p => p.CanReview, true));

        revisions.Find("[data-testid='document-revision-accept']").ClassList
            .Should().Contain("tm-document-revision-panel__action").And.Contain("tm-document-revision-panel__action--accept");
        revisions.Find("[data-testid='document-revision-reject-all']").ClassList
            .Should().Contain("tm-document-revision-panel__batch-action--reject");
    }

    [Fact]
    public void EditorMarkup_NeverUsesThePivotTableButtonClasses()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("tm-pivot-btn", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty("the pivot-table button classes belong to TmPivotTable; the editor uses tm-btn");
    }

    [Fact]
    public void ImageDialogActions_AreLibraryButtons()
    {
        var root = FindRepositoryRoot();
        var editor = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditor.razor"));

        editor.Should().Contain("class=\"tm-btn tm-btn-sm tm-btn-secondary\"", "the cancel action of the image URL dialog");
        editor.Should().Contain("class=\"tm-btn tm-btn-sm tm-btn-primary\"", "the insert action of the image URL dialog");
    }

    [Fact]
    public void LinkDialog_ButtonsAndInputs_AreLibraryControls()
    {
        var root = FindRepositoryRoot();
        var toolbar = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditorToolbar.razor"));
        var start = toolbar.IndexOf("document-link-dialog", StringComparison.Ordinal);
        var end = toolbar.IndexOf("</section>", start, StringComparison.Ordinal);
        var dialog = toolbar[start..end];

        System.Text.RegularExpressions.Regex.Matches(dialog, "<button ").Count.Should().BeGreaterThan(0);
        foreach (System.Text.RegularExpressions.Match button in System.Text.RegularExpressions.Regex.Matches(dialog, "<button [^>]*>"))
        {
            button.Value.Should().Contain("tm-btn", "the link dialog buttons carry the library class");
        }

        foreach (System.Text.RegularExpressions.Match input in System.Text.RegularExpressions.Regex.Matches(dialog, "<input [^>]*"))
        {
            input.Value.Should().Contain("class=\"tm-input\"");
        }
    }

    private static void AssertPrimary(AngleSharp.Dom.IElement button, string? aliasClass)
    {
        button.ClassList.Should().Contain("tm-btn").And.Contain("tm-btn-primary");
        if (aliasClass is not null)
        {
            button.ClassList.Should().Contain(aliasClass);
        }
    }

    private static DocumentRevision PendingRevision() => new()
    {
        Id = "revision-1",
        Type = DocumentRevisionType.Insertion,
        Action = DocumentRevisionAction.Pending,
        Author = new DocumentRevisionAuthor { Id = "reviewer-1", DisplayName = "reviewer-1" },
        Range = new DocumentRevisionRange { BlockId = "block-1" },
        PayloadJson = "Inserted"
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
