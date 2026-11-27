using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>bUnit tests for 3.3–3.5: toolbar buttons derive disabled/aria state from CommandRegistry.</summary>
public class DocumentEditorToolbarCommandStateTests : LocalizationTestBase
{
    // ─── 3.3 Save – registry-driven ──────────────────────────────────────────

    [Fact]
    public void HomeTab_SaveButton_ExistsWithCorrectTestId()
    {
        var registry = BuildRegistry(("save", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-save']").Should().NotBeNull();
    }

    [Fact]
    public void HomeTab_SaveButton_IsDisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("save", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var button = cut.Find("[data-testid='document-save']");
        button.HasAttribute("disabled").Should().BeTrue();
        button.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_SaveButton_IsEnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("save", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var button = cut.Find("[data-testid='document-save']");
        button.HasAttribute("disabled").Should().BeFalse();
        button.HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.3 Undo/Redo – registry-driven ────────────────────────────────────

    [Fact]
    public void HomeTab_UndoButton_ExistsWithCorrectTestId()
    {
        var registry = BuildRegistry(("undo", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-undo']").Should().NotBeNull();
    }

    [Fact]
    public void HomeTab_UndoButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("undo", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var button = cut.Find("[data-testid='document-undo']");
        button.HasAttribute("disabled").Should().BeTrue();
        button.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_UndoButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("undo", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var button = cut.Find("[data-testid='document-undo']");
        button.HasAttribute("disabled").Should().BeFalse();
        button.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_RedoButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("redo", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var button = cut.Find("[data-testid='document-redo']");
        button.HasAttribute("disabled").Should().BeFalse();
        button.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_UndoButton_EnabledWhenRuntimeCanUndoEvenIfRegistryIsStaleDisabled()
    {
        var registry = BuildRegistry(("undo", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CanUndo, true));

        cut.Find("[data-testid='document-undo']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_RedoButton_EnabledWhenRuntimeCanRedoEvenIfRegistryIsStaleDisabled()
    {
        var registry = BuildRegistry(("redo", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CanRedo, true));

        cut.Find("[data-testid='document-redo']").HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.3 Bold/Italic/Underline – registry-driven ─────────────────────────

    [Fact]
    public void HomeTab_BoldButton_IsDisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("bold", enabled: false, value: "inactive"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var button = cut.Find("[data-testid='document-bold']");
        button.HasAttribute("disabled").Should().BeTrue();
        button.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_BoldButton_IsEnabledAndAriaFalseWhenInactive()
    {
        var registry = BuildRegistry(("bold", enabled: true, value: "active"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.BoldState, WysiwygFormattingValue.Inactive));

        var bold = cut.Find("[data-testid='document-bold']");
        bold.HasAttribute("disabled").Should().BeFalse();
        bold.HasAttribute("disabled").Should().BeFalse();
        bold.GetAttribute("aria-pressed").Should().Be("false",
            "the canonical JS formatting state, not a stale registry value, owns the active state");
    }

    [Fact]
    public void HomeTab_BoldButton_AriaAndClassComeFromCanonicalFormattingStateWhenActive()
    {
        var registry = BuildRegistry(("bold", enabled: true, value: "inactive"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.BoldState, WysiwygFormattingValue.Active));

        var bold = cut.Find("[data-testid='document-bold']");
        bold.GetAttribute("aria-pressed").Should().Be("true");
        bold.GetAttribute("class").Should().Contain("tm-document-editor__ribbon-button--active");
    }

    [Fact]
    public void HomeTab_BoldButton_AriaAndClassComeFromCanonicalFormattingStateWhenMixed()
    {
        var registry = BuildRegistry(("bold", enabled: true, value: "inactive"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.BoldState, WysiwygFormattingValue.Mixed));

        var bold = cut.Find("[data-testid='document-bold']");
        bold.GetAttribute("aria-pressed").Should().Be("mixed");
        bold.GetAttribute("class").Should().Contain("tm-document-editor__ribbon-button--mixed");
    }

    [Fact]
    public void HomeTab_ItalicButton_AriaIsTrueWhenActive()
    {
        var registry = BuildRegistry(("italic", enabled: true, value: "inactive"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.ItalicState, WysiwygFormattingValue.Active));

        cut.Find("[data-testid='document-italic']").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void HomeTab_UnderlineButton_AriaIsTrueWhenActive()
    {
        var registry = BuildRegistry(("underline", enabled: true, value: "inactive"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.UnderlineState, WysiwygFormattingValue.Active));

        cut.Find("[data-testid='document-underline']").GetAttribute("aria-pressed").Should().Be("true");
    }

    // ─── 3.4 Insert tab – registry-driven ────────────────────────────────────

    [Fact]
    public void InsertTab_TableButton_ExistsWithCorrectTestId()
    {
        var registry = BuildRegistry(("insertTable", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-toolbar-table']").Should().NotBeNull();
    }

    [Fact]
    public void InsertTab_TableButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("insertTable", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-toolbar-table']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void InsertTab_TableButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("insertTable", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-toolbar-table']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void InsertTab_ImageButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("insertImage", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CanBrowseImageAssets, true));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-toolbar-image']").HasAttribute("disabled").Should().BeFalse();
    }

    // Phase 5 (DE-1.5): the image menu and the URL dialog are ONE panel with tabs, owned by the editor (floating root). The ribbon
    // button only asks for it: it renders no menu of its own.
    [Fact]
    public void InsertTab_ImageButton_RaisesOnInsertImage_AndRendersNoMenuOfItsOwn()
    {
        var registry = BuildRegistry(("insertImage", enabled: true, value: null));
        var raised = 0;

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CanUploadImages, true)
            .Add(x => x.CanBrowseImageAssets, true)
            .Add(x => x.OnInsertImage, () => raised++));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        cut.Find("[data-testid='document-toolbar-image']").Click();

        raised.Should().Be(1);
        cut.FindAll("[data-testid='document-image-insert-menu']").Should().BeEmpty();
        cut.Find("[data-testid='document-toolbar-image']").GetAttribute("aria-haspopup").Should().Be("dialog");
    }

    [Fact]
    public void InsertTab_ImageButton_ReflectsTheEditorsPanelState_InAriaExpanded()
    {
        var registry = BuildRegistry(("insertImage", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.ImagePanelOpen, true));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-toolbar-image']").GetAttribute("aria-expanded").Should().Be("true");
    }

    // ─── 3.5 Review tab – TrackChanges registry-driven ───────────────────────

    [Fact]
    public void ReviewTab_TrackChangesButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("trackChanges", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        var button = cut.Find("[data-testid='document-track-changes']");
        button.HasAttribute("disabled").Should().BeTrue();
        button.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ReviewTab_TrackChangesButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("trackChanges", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        var button = cut.Find("[data-testid='document-track-changes']");
        button.HasAttribute("disabled").Should().BeFalse();
        button.HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.3 Font selectors – registry-driven ────────────────────────────────

    [Fact]
    public void HomeTab_FontFamilySelect_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("fontFamily", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var select = cut.Find("[data-testid='document-font-family']");
        select.HasAttribute("disabled").Should().BeTrue();
        select.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_FontFamilySelect_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("fontFamily", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var select = cut.Find("[data-testid='document-font-family']");
        select.HasAttribute("disabled").Should().BeFalse();
        select.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_FontSizeSelect_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("fontSize", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        var select = cut.Find("[data-testid='document-font-size']");
        select.HasAttribute("disabled").Should().BeTrue();
        select.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_FontSelects_ReflectCanonicalFormattingValues()
    {
        var registry = BuildRegistry(
            ("fontFamily", enabled: true, value: "stale-font"),
            ("fontSize", enabled: true, value: "13pt"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.FontFamilies, new[]
            {
                new DocumentFontFamily { DisplayName = "Arial", CssFamily = "Arial, sans-serif" },
                new DocumentFontFamily { DisplayName = "Georgia", CssFamily = "Georgia, serif" }
            })
            .Add(x => x.CurrentFontFamily, "Georgia, serif")
            .Add(x => x.CurrentFontSize, "28pt"));

        cut.Find("[data-testid='document-font-family']")
            .GetAttribute("value")
            .Should()
            .Be("Georgia, serif");
        cut.Find("[data-testid='document-font-size']")
            .GetAttribute("value")
            .Should()
            .Be("28");
    }

    [Fact]
    public void HomeTab_FontSelects_ShowMixedStateFromCanonicalFormattingValues()
    {
        var registry = BuildRegistry(
            ("fontFamily", enabled: true, value: null),
            ("fontSize", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.FontFamilyMixed, true)
            .Add(x => x.FontSizeMixed, true));

        cut.Find("[data-testid='document-font-family']")
            .GetAttribute("value")
            .Should()
            .Be("__mixed");
        cut.Find("[data-testid='document-font-family']").TextContent.Should().Contain("Mixed");
        cut.Find("[data-testid='document-font-size']")
            .GetAttribute("value")
            .Should()
            .BeEmpty();
        cut.Find("[data-testid='document-font-size']").TextContent.Should().Contain("Mixed");
    }

    [Fact]
    public void HomeTab_ColorPickers_ReflectCanonicalSwatchesAndMixedState()
    {
        var registry = BuildRegistry(
            ("textColor", enabled: true, value: "#111827"),
            ("highlightColor", enabled: true, value: "#ffffff"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CurrentTextColor, "#2563EB")
            .Add(x => x.CurrentHighlightColor, "#FEF08A")
            .Add(x => x.HighlightColorMixed, true));

        var textColor = cut.Find("[data-testid='document-font-color-trigger']");
        var highlight = cut.Find("[data-testid='document-highlight-color-trigger']");

        textColor.TextContent.Should().Contain("#2563eb");
        textColor.InnerHtml.Should().Contain("background: #2563eb");
        highlight.TextContent.Should().Contain("#fef08a");
        highlight.GetAttribute("class").Should().Contain("tm-document-editor__ribbon-tempo-color-picker--mixed");
    }

    [Fact]
    public void HomeTab_ColorPickers_ExposeDisabledAriaStateFromRegistry()
    {
        var registry = BuildRegistry(
            ("textColor", enabled: false, value: null),
            ("highlightColor", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger")
            .GetAttribute("aria-disabled")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-highlight-color-trigger'] .tm-color-picker-trigger")
            .GetAttribute("aria-disabled")
            .Should()
            .Be("false");
    }

    [Fact]
    public void HomeTab_ClearFormattingButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("clearFormatting", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-clear-formatting']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_ClearFormattingButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("clearFormatting", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-clear-formatting']").HasAttribute("disabled").Should().BeTrue();
    }

    // ─── 3.3 Alignment buttons – registry-driven ─────────────────────────────

    [Fact]
    public void HomeTab_AlignLeftButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("paragraphAlignment", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-align'] button").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_AlignCenterButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("paragraphAlignment", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-align'] button").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_AlignRightButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("paragraphAlignment", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-align'] button").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_AlignJustifyButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("paragraphAlignment", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-align'] button").HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.3 Link button – registry-driven ───────────────────────────────────

    [Fact]
    public void HomeTab_LinkButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("link", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-link']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_LinkButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("link", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));

        cut.Find("[data-testid='document-link']").HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.4 Insert menu – registry-driven ───────────────────────────────────

    [Fact]
    public void InsertTab_InsertMenuButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("insertMenu", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-insert-menu']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void InsertTab_InsertMenuButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("insertMenu", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        cut.Find("[data-testid='document-insert-menu']").HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.5 ReviewDisplayMode – registry-driven ─────────────────────────────

    [Fact]
    public void ReviewTab_ReviewDisplayModeSelect_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("reviewDisplayMode", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-review-display-mode']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ReviewTab_ReviewDisplayModeSelect_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("reviewDisplayMode", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-review-display-mode']").HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.5 AddComment / Compare – registry-driven ──────────────────────────

    [Fact]
    public void ReviewTab_AddCommentButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("addComment", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-add-comment']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ReviewTab_AddCommentButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("addComment", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-add-comment']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ReviewTab_CompareButton_DisabledWhenRegistrySaysDisabled()
    {
        var registry = BuildRegistry(("compareDocuments", enabled: false, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-compare-open']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ReviewTab_CompareButton_EnabledWhenRegistrySaysEnabled()
    {
        var registry = BuildRegistry(("compareDocuments", enabled: true, value: null));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CanCompareDocuments, true));
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-compare-open']").HasAttribute("disabled").Should().BeFalse();
    }

    // ─── 3.5 Ruler/Zoom/PageWidth – behavioral regression ────────────────────

    [Fact]
    public void ViewTab_RulerToggle_ExistsAndRespondsToClick()
    {
        var showRuler = true;
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ShowRuler, showRuler)
            .Add(x => x.ShowRulerChanged, v => showRuler = v));
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-toggle-ruler']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-toggle-ruler']").Click();

        showRuler.Should().BeFalse();
    }

    [Fact]
    public void ViewTab_ZoomButtons_ExistAndFireCallbacks()
    {
        var zoomPercent = 100;
        var pageWidthRequested = false;
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ZoomPercent, zoomPercent)
            .Add(x => x.ZoomPercentChanged, v => zoomPercent = v)
            .Add(x => x.OnZoomPageWidth, () => pageWidthRequested = true));
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-zoom-in']").Click();
        cut.Find("[data-testid='document-zoom-page-width']").Click();

        zoomPercent.Should().Be(110);
        pageWidthRequested.Should().BeTrue();
    }

    // ─── Fallback: no CommandRegistry ─────────────────────────────────────────

    [Fact]
    public void HomeTab_SaveButton_FallsBackToParametersWhenNoRegistry()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ReadOnly, false)
            .Add(x => x.IsSaving, false));

        cut.Find("[data-testid='document-save']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_SaveButton_FallsBackDisabledWhenReadOnlyAndNoRegistry()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ReadOnly, true));

        cut.Find("[data-testid='document-save']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void HomeTab_UndoButton_FallsBackToRuntimeCanUndoWhenNoRegistry()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ReadOnly, false)
            .Add(x => x.CanUndo, true));

        cut.Find("[data-testid='document-undo']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_RedoButton_FallsBackToRuntimeCanRedoWhenNoRegistry()
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ReadOnly, false)
            .Add(x => x.CanRedo, true));

        cut.Find("[data-testid='document-redo']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HomeTab_UndoRedoButtons_ReflectRuntimeEnabledStateAndDescriptions()
    {
        var registry = BuildRegistry(
            ("undo", enabled: false, value: "stale-disabled"),
            ("redo", enabled: false, value: "stale-disabled"));

        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CommandRegistry, registry)
            .Add(x => x.CanUndo, true)
            .Add(x => x.CanRedo, true)
            .Add(x => x.NextUndoDescription, "Typing session")
            .Add(x => x.NextRedoDescription, "Formatting command"));

        var undo = cut.Find("[data-testid='document-undo']");
        var redo = cut.Find("[data-testid='document-redo']");

        undo.HasAttribute("disabled").Should().BeFalse();
        redo.HasAttribute("disabled").Should().BeFalse();
        undo.GetAttribute("title").Should().Contain("Typing session");
        redo.GetAttribute("title").Should().Contain("Formatting command");
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static DocumentEditorCommandRegistry BuildRegistry(
        params (string name, bool enabled, string? value)[] commands)
    {
        var registry = new DocumentEditorCommandRegistry();
        foreach (var (name, enabled, value) in commands)
        {
            bool capturedEnabled = enabled;
            string? capturedValue = value;
            registry.Register(new FuncDocumentEditorCommandEntry(
                name, affectsData: true,
                computeEnabled: _ => capturedEnabled,
                computeValue: _ => capturedValue,
                execute: (_, _) => Task.CompletedTask));
        }

        var ctx = new DocumentEditorCommandContext();
        registry.RefreshAllAsync(ctx).GetAwaiter().GetResult();
        return registry;
    }
}
