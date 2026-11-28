using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 5 (G6 / Q2-12): the version, compare and debug dialogs are core TmModal instances and the editor's own
/// focus trap is gone - the focus trap comes from the core only.
/// </summary>
public sealed class DocumentEditorDialogsTmModalTests : LocalizationTestBase
{
    private static void AssertCoreModal(IRenderedComponent<IComponent> cut, string testId)
    {
        var overlay = cut.Find($"[data-testid='{testId}']");
        overlay.ClassList.Should().Contain("tm-modal-overlay");
        var dialog = overlay.QuerySelector("[role='dialog']");
        dialog.Should().NotBeNull();
        dialog!.GetAttribute("aria-modal").Should().Be("true");
        dialog.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace("the title names the dialog");
    }

    [Fact]
    public void VersionDialog_IsACoreModal_AsASheetOnMobile_WithTheTestIdsKept()
    {
        var cut = Render<TmDocumentVersionDialog>(p => p.Add(x => x.RequireMajorDescription, true));

        AssertCoreModal(cut, "document-version-dialog");
        foreach (var id in new[] { "document-version-kind", "document-version-label", "document-version-description", "document-version-create-submit", "document-version-cancel", "document-version-dialog-close" })
        {
            cut.FindAll($"[data-testid='{id}']").Should().ContainSingle(id);
        }

        cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.MobilePresentation
            .Should().Be(Tempo.Blazor.Components.Feedback.MobilePresentation.Auto, "a sheet on a mobile layout, a dialog otherwise");
        cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.FooterLayout
            .Should().Be(Tempo.Blazor.Components.Feedback.FooterLayout.Inline);
    }

    [Fact]
    public void VersionDialog_MajorWithoutDescription_ShowsTheError_AndTheModalFooterHoldsTheActions()
    {
        var created = 0;
        var cut = Render<TmDocumentVersionDialog>(p => p
            .Add(x => x.RequireMajorDescription, true)
            .Add(x => x.OnCreate, EventCallback.Factory.Create<DocumentVersionDialogResult>(this, _ => created++)));

        cut.Find("[data-testid='document-version-kind']").Change(DocumentVersionKind.Major.ToString());
        cut.Find("form.tm-document-version-dialog__form").Submit();

        cut.Find("[data-testid='document-version-error']").GetAttribute("role").Should().Be("alert");
        created.Should().Be(0);
        cut.FindAll(".tm-modal-footer [data-testid='document-version-cancel'], .tm-modal-footer [data-testid='document-version-create-submit']").Should().HaveCount(2);
    }

    [Fact]
    public void CompareDialog_IsACoreModal_FullscreenOnMobile_WithTheTestIdsKept()
    {
        var cut = Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()));

        AssertCoreModal(cut, "document-compare-dialog");
        cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.MobilePresentation
            .Should().Be(Tempo.Blazor.Components.Feedback.MobilePresentation.AutoFullscreen);
        // Phase 7: the source pickers open under their cards ("Change..."), so their ids exist only once the card is expanded.
        cut.Find("[data-testid='document-compare-base-change']").Click();
        cut.Find("[data-testid='document-compare-target-change']").Click();
        foreach (var id in new[] { "document-compare-run", "document-compare-close", "document-compare-base-source", "document-compare-target-source", "document-compare-empty", "document-compare-base-card", "document-compare-target-card" })
        {
            cut.FindAll($"[data-testid='{id}']").Should().ContainSingle(id);
        }
    }

    // Phase 13: a phone dialog keeps its footer above the on-screen keyboard, and the compare dialog answers to its OWN width.
    [Fact]
    public void VersionAndCompareDialogs_OptIntoTheVirtualKeyboardTracking()
    {
        Render<TmDocumentVersionDialog>().FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.TrackVirtualKeyboard
            .Should().BeTrue("the sheet footer (Create / Cancel) must stay above the keyboard while a field is focused");
        Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()))
            .FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.TrackVirtualKeyboard
            .Should().BeTrue();
    }

    [Fact]
    public void CompareDialog_SourcesLiveInsideTheContentWrapper()
    {
        var cut = Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()));

        var wrapper = cut.Find(".tm-modal-body > .tm-document-compare-dialog__content");
        wrapper.QuerySelector(".tm-document-compare-dialog__sources").Should().NotBeNull(
            "the content wrapper (the readable column of a pinned phone layout) holds the sources grid");
    }

    // Phase 13 fix round (UX 5): the first thing a dialog asks for is where the focus lands.
    [Fact]
    public void CompareDialog_InitialFocus_IsTheChangeButtonOfTheFirstMissingSource()
    {
        var cut = Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()));

        var selector = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.InitialFocusSelector;
        selector.Should().NotBeNullOrWhiteSpace();
        cut.FindAll(selector!).Should().ContainSingle("the target source is the one that names nothing yet")
            .Which.GetAttribute("data-testid").Should().Be("document-compare-target-change");
    }

    [Fact]
    public void VersionDialog_InitialFocus_IsTheLabelField()
    {
        var cut = Render<TmDocumentVersionDialog>();

        var id = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.InitialFocusTargetId;
        id.Should().NotBeNullOrWhiteSpace();
        cut.Find($"[id='{id}']").GetAttribute("data-testid").Should().Be("document-version-label",
            "the label is the first thing the reader types; the kind select keeps its sensible default");
    }

    [Fact]
    public void VersionDialog_Footer_IsCancelThenPrimary_WithABorderedCancel()
    {
        var cut = Render<TmDocumentVersionDialog>(p => p.Add(x => x.RequireMajorDescription, false));

        var buttons = cut.FindAll(".tm-modal-footer button").Select(b => b.GetAttribute("data-testid")).ToList();
        buttons.Should().Equal("document-version-cancel", "document-version-create-submit");
        cut.Find("[data-testid='document-version-cancel']").ClassList.Should().Contain("tm-btn-secondary").And.NotContain("tm-btn-ghost");
        cut.Find("[data-testid='document-version-create-submit']").ClassList.Should().Contain("tm-btn-primary");
    }

    [Fact]
    public void CompareDialog_EmptyState_IsOneFramedCard_NotABlankBlock()
    {
        var cut = Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()));

        var ready = cut.Find("[data-testid='document-compare-empty']");
        ready.TagName.ToLowerInvariant().Should().Be("section");
        ready.QuerySelector("h4")!.TextContent.Should().Be("Choose both sources to compare.", "while a source is missing the card names what is missing, not 'Ready'");
        ready.QuerySelector(".tm-document-compare-dialog__ready-hint svg").Should().BeNull("the 'Click Compare' hint only appears when Compare can be clicked");
        cut.Find(".tm-document-compare-dialog__intro").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll(".tm-document-editor__state").Should().BeEmpty("the old stray state block is gone");
    }

    [Fact]
    public void DebugModals_AreCoreModals_WithFooterClose_AndTheBemAliases()
    {
        var json = Render<TmDocumentJsonDebugModal>(p => p.Add(x => x.IsOpen, true).Add(x => x.Json, "{}"));
        AssertCoreModal(json, "document-json-debug-modal");
        json.Find("[data-testid='document-json-debug-modal']").ClassList.Should().Contain("tm-document-debug-modal").And.Contain("tm-document-json-debug-modal");
        json.FindAll(".tm-modal-footer button").Should().ContainSingle();

        var clipboard = Render<TmDocumentClipboardHtmlDebugModal>(p => p.Add(x => x.IsOpen, true).Add(x => x.Html, "<p/>"));
        AssertCoreModal(clipboard, "document-clipboard-html-debug-modal");
    }

    [Fact]
    public void TheEditorsOwnFocusTrap_NoLongerExists()
    {
        typeof(TmDocumentVersionDialog).Assembly.GetTypes()
            .Where(type => type.Name.Contains("FocusTrap", StringComparison.Ordinal))
            .Should().BeEmpty("DocumentEditorFocusTrapController was deleted - the focus trap is the core TmFocusScope of TmModal");

        var root = FindRepositoryRoot();
        File.Exists(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "DocumentEditorFocusTrapController.cs")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("FocusTrapController", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
