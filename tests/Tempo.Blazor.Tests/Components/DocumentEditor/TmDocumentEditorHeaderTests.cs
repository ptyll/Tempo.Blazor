using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>The document header (phase 3, DE-1.1) in isolation: the save states, the command gating it shares with the ribbon and the slots.</summary>
public class TmDocumentEditorHeaderTests : LocalizationTestBase
{
    [Fact]
    public void NoSaveYet_ShowsNoStatus()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p.Add(c => c.Title, "Aurora"));

        cut.Find("[data-testid='document-header-status']").GetAttribute("data-state").Should().Be("none");
        cut.Find("[data-testid='document-header-status']").TextContent.Trim().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false, "saving", "Saving")]
    [InlineData(false, true, "dirty", "Unsaved changes")]
    public void StatusFollowsTheSaveState(bool saving, bool dirty, string state, string text)
    {
        var cut = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.IsSaving, saving)
            .Add(c => c.IsDirty, dirty)
            .Add(c => c.LastSavedAt, DateTimeOffset.UtcNow));

        var status = cut.Find("[data-testid='document-header-status']");
        status.GetAttribute("data-state").Should().Be(state, "saving beats dirty beats saved");
        status.TextContent.Should().Contain(text);
    }

    [Fact]
    public void Saved_ShowsTheLastSavedTimeUnderTheStableTestId()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.LastSavedAt, new DateTimeOffset(2026, 5, 22, 10, 24, 0, TimeSpan.Zero)));

        cut.Find("[data-testid='document-last-saved']").TextContent.Should().Contain("Last saved");
        cut.Find("[data-testid='document-header-status']").GetAttribute("data-state").Should().Be("saved");
    }

    [Fact]
    public void ReadOnly_DisablesSaveUndoAndRedo_WithAReasonInTheTooltip()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.ReadOnly, true)
            .Add(c => c.CanUndo, true)
            .Add(c => c.CanRedo, true));

        foreach (var id in new[] { "document-save", "document-undo", "document-redo" })
        {
            var button = cut.Find($"[data-testid='{id}']");
            button.HasAttribute("disabled").Should().BeTrue($"{id} is disabled in a read-only editor");
            button.HasAttribute("aria-disabled").Should().BeFalse("the native disabled attribute is the one idiom (docs/buttons.md)");
            button.GetAttribute("title").Should().Contain(":", "the tooltip names the reason");
        }
    }

    [Fact]
    public void Commands_RaiseTheirCallbacks_WhenEnabled()
    {
        var raised = new List<string>();
        var cut = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.CanUndo, true)
            .Add(c => c.CanRedo, true)
            .Add(c => c.ShowPrintPreview, true)
            .Add(c => c.CanExportPdf, true)
            .Add(c => c.OnSearch, () => { raised.Add("search"); })
            .Add(c => c.OnUndo, () => { raised.Add("undo"); })
            .Add(c => c.OnRedo, () => { raised.Add("redo"); })
            .Add(c => c.OnSave, () => { raised.Add("save"); })
            .Add(c => c.OnOpenPrintPreview, () => { raised.Add("preview"); })
            .Add(c => c.OnExportPdf, () => { raised.Add("pdf"); }));

        foreach (var id in new[] { "document-header-search", "document-undo", "document-redo", "document-header-print-preview", "document-header-export-pdf", "document-save" })
        {
            cut.Find($"[data-testid='{id}']").Click();
        }

        raised.Should().Equal("search", "undo", "redo", "preview", "pdf", "save");
    }

    [Fact]
    public void Save_IsDisabledWhileASaveIsInFlight()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p.Add(c => c.Title, "Aurora").Add(c => c.IsSaving, true));

        cut.Find("[data-testid='document-save']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void PrintPreviewAndPdf_AreOfferedOnlyWithTheirCapability()
    {
        var bare = Render<TmDocumentEditorHeader>(p => p.Add(c => c.Title, "Aurora"));
        bare.FindAll("[data-testid='document-header-print-preview']").Should().BeEmpty();
        bare.FindAll("[data-testid='document-header-export-pdf']").Should().BeEmpty();

        var capable = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.ShowPrintPreview, true)
            .Add(c => c.PrintPreviewActive, true)
            .Add(c => c.CanExportPdf, true)
            .Add(c => c.IsExportingPdf, true));
        capable.Find("[data-testid='document-header-print-preview']").GetAttribute("aria-pressed").Should().Be("true");
        capable.Find("[data-testid='document-header-export-pdf']").HasAttribute("disabled").Should().BeTrue("an export is in flight");
        capable.Find("[data-testid='document-header-export-pdf']").GetAttribute("aria-busy").Should().Be("true");
    }

    [Fact]
    public void IconOnlyActions_KeepTheirAccessibleName()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p.Add(c => c.Title, "Aurora").Add(c => c.ShowPrintPreview, true));

        // The visible label may be dropped by a container query on a narrow header; the name must not go with it.
        foreach (var id in new[] { "document-header-search", "document-header-print-preview", "document-save" })
        {
            cut.Find($"[data-testid='{id}']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace($"{id} is named for assistive technology");
        }
    }

    [Fact]
    public void Commands_AdvertiseTheirShortcuts()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p.Add(c => c.Title, "Aurora"));

        cut.Find("[data-testid='document-undo']").GetAttribute("aria-keyshortcuts").Should().Be("Control+Z");
        cut.Find("[data-testid='document-redo']").GetAttribute("aria-keyshortcuts").Should().Be("Control+Y Control+Shift+Z");
        cut.Find("[data-testid='document-save']").GetAttribute("aria-keyshortcuts").Should().Be("Control+S");
        cut.FindAll("[aria-disabled]").Should().BeEmpty("an enabled header has no aria-disabled at all");
    }

    [Fact]
    public void ModeLabel_AndSubtitle_RenderOnlyWhenGiven()
    {
        var plain = Render<TmDocumentEditorHeader>(p => p.Add(c => c.Title, "Aurora"));
        plain.FindAll("[data-testid='document-header-mode']").Should().BeEmpty();
        plain.FindAll("[data-testid='document-header-subtitle']").Should().BeEmpty();

        var both = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.Subtitle, "Draft")
            .Add(c => c.ModeLabel, "Previewing version 2"));
        both.Find("[data-testid='document-header-mode']").TextContent.Should().Be("Previewing version 2");
        both.Find("[data-testid='document-header-subtitle']").TextContent.Should().Be("Draft");
    }

    [Fact]
    public void HostSlots_Render_AndTheHeaderIsAGroupLandmarkForF6()
    {
        var cut = Render<TmDocumentEditorHeader>(p => p
            .Add(c => c.Title, "Aurora")
            .Add(c => c.HeaderStart, (RenderFragment)(b => b.AddMarkupContent(0, "<i data-testid='slot-start'></i>")))
            .Add(c => c.HeaderActions, (RenderFragment)(b => b.AddMarkupContent(0, "<i data-testid='slot-end'></i>"))));

        cut.Find("[data-testid='document-header-start'] [data-testid='slot-start']").Should().NotBeNull();
        cut.Find("[data-testid='document-header-host-actions'] [data-testid='slot-end']").Should().NotBeNull();
        var header = cut.Find("[data-testid='document-header']");
        header.GetAttribute("role").Should().Be("group");
        header.GetAttribute("data-focus-region").Should().Be("header");
        header.GetAttribute("aria-label").Should().Be("Document header");
    }
}
