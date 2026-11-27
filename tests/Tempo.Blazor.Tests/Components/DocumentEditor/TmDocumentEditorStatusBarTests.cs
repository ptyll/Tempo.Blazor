using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentEditorStatusBarTests : LocalizationTestBase
{
    // ── Pending indicator ───────────────────────────────────────────────────

    [Fact]
    public void StatusBar_NoPending_DoesNotShowPendingSpan()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 0));

        cut.FindAll("[data-testid='document-pending-status']").Should().BeEmpty();
    }

    [Fact]
    public void StatusBar_PendingCountOne_ShowsPendingSpan()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 1)
             .Add(x => x.PendingMessage, "Saving..."));

        cut.Find("[data-testid='document-pending-status']").Should().NotBeNull();
    }

    [Fact]
    public void StatusBar_PendingMessage_ShowsMessageText()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 1)
             .Add(x => x.PendingMessage, "Saving..."));

        cut.Find("[data-testid='document-pending-status']").TextContent.Trim()
           .Should().Contain("Saving...");
    }

    [Fact]
    public void StatusBar_AutosaveWaiting_ShowsPendingText()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 1)
             .Add(x => x.PendingMessage, "Autosave pending..."));

        cut.Find("[data-testid='document-pending-status']").TextContent.Trim()
           .Should().Contain("Autosave pending...");
    }

    [Fact]
    public void StatusBar_AutosaveSaving_ShowsSavingText()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 1)
             .Add(x => x.PendingMessage, "Saving..."));

        cut.Find("[data-testid='document-pending-status']").TextContent.Trim()
           .Should().Contain("Saving...");
    }

    [Fact]
    public void StatusBar_SaveErrorWithRetry_ShowsRetryButton()
    {
        var called = false;
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.SaveMessage, "Save failed.")
             .Add(x => x.CanRetrySave, true)
             .Add(x => x.OnRetrySave, () => called = true));

        cut.Find("[data-testid='document-save-retry']").Click();

        called.Should().BeTrue();
    }

    [Fact]
    public void StatusBar_SaveErrorWithoutRetry_HidesRetryButton()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.SaveMessage, "Save failed.")
             .Add(x => x.CanRetrySave, false));

        cut.FindAll("[data-testid='document-save-retry']").Should().BeEmpty();
    }

    [Fact]
    public void StatusBar_PendingCountWithoutMessage_ShowsGenericPendingText()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 2)
             .Add(x => x.PendingMessage, (string?)null));

        cut.Find("[data-testid='document-pending-status']").TextContent.Trim()
           .Should().Contain("2");
    }

    [Fact]
    public void StatusBar_PendingActive_HidesDirtyStatus()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 1)
             .Add(x => x.PendingMessage, "Saving...")
             .Add(x => x.IsDirty, true));

        // Pending takes priority — dirty span should not appear
        cut.FindAll("[data-testid='document-dirty-status']").Should().BeEmpty();
    }

    [Fact]
    public void StatusBar_NoPending_ShowsDirtyWhenSet()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.PendingCount, 0)
             .Add(x => x.IsDirty, true));

        cut.Find("[data-testid='document-dirty-status']").Should().NotBeNull();
    }

    // ── Save message ────────────────────────────────────────────────────────

    [Fact]
    public void StatusBar_SaveMessage_ShowsSaveMessageSpan()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.SaveMessage, "Saved!"));

        cut.Find("[data-testid='document-save-message']").TextContent.Trim()
           .Should().Be("Saved!");
    }

    [Fact]
    public void StatusBar_NoSaveMessage_DoesNotShowSaveMessageSpan()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.SaveMessage, (string?)null));

        cut.FindAll("[data-testid='document-save-message']").Should().BeEmpty();
    }

    // ── Metrics ─────────────────────────────────────────────────────────────

    [Fact]
    public void StatusBar_RendersWordAndPageCount()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.WordCount, 42)
             .Add(x => x.PageCount, 3));

        cut.Find("[data-testid='document-status-word-count']").TextContent
           .Should().Contain("42");
        cut.Find("[data-testid='document-status-page-count']").TextContent
           .Should().Contain("3");
    }

    [Fact]
    public void StatusBar_ZoomLabel_IsDisplayed()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.ZoomLabel, "75%"));

        cut.Find("[data-testid='document-status-zoom']").TextContent
           .Should().Contain("75%");
    }

    // ── Runtime message ─────────────────────────────────────────────────────

    [Fact]
    public void StatusBar_RuntimeMessage_ShowsMessageSpan()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.RuntimeMessage, "Editor recovered."));

        cut.Find("[data-testid='document-runtime-message']").TextContent.Trim()
           .Should().Contain("Editor recovered.");
    }

    [Fact]
    public void StatusBar_NoRuntimeMessage_DoesNotShowMessageSpan()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.RuntimeMessage, (string?)null));

        cut.FindAll("[data-testid='document-runtime-message']").Should().BeEmpty();
    }

    [Fact]
    public void StatusBar_RuntimeMessage_RecoveredHasRecoveredClass()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.RuntimeMessage, "Recovered!")
             .Add(x => x.RuntimeFailed, false));

        cut.Find("[data-testid='document-runtime-message']").ClassList
           .Should().Contain("tm-document-editor__runtime-message--recovered");
    }

    [Fact]
    public void StatusBar_RuntimeMessage_FailedHasFailedClass()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.RuntimeMessage, "Recovery failed!")
             .Add(x => x.RuntimeFailed, true));

        cut.Find("[data-testid='document-runtime-message']").ClassList
           .Should().Contain("tm-document-editor__runtime-message--failed");
    }

    [Fact]
    public void StatusBar_RuntimeMessage_HasAlertRole()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p =>
            p.Add(x => x.RuntimeMessage, "Alert!"));

        cut.Find("[data-testid='document-runtime-message']")
           .GetAttribute("role").Should().Be("alert");
    }

    // ── Phase 7 (DE-1.8): page X of Y, plural counts, zoom and fullscreen controls ─────────────────────

    [Fact]
    public void StatusBar_ShowsPageXOfY_FromTheActivePage()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.CurrentPage, 2).Add(x => x.PageCount, 6));

        cut.Find("[data-testid='document-status-page-count']").TextContent.Should().Be("Page 2 of 6");
    }

    [Theory]
    [InlineData(1, "1 word")]
    [InlineData(0, "0 words")]
    [InlineData(42, "42 words")]
    public void StatusBar_WordCount_UsesThePluralFamily(int words, string expected)
    {
        var cut = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.WordCount, words));

        cut.Find("[data-testid='document-status-word-count']").TextContent.Should().Be(expected);
    }

    [Fact]
    public void StatusBar_ZoomButtons_StepByTenPercent_AndRaiseTheNewZoom()
    {
        var raised = new List<int>();
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.ZoomPercent, 100)
            .Add(x => x.ZoomLabel, "100%")
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, raised.Add)));

        cut.Find("[data-testid='document-status-zoom-in']").Click();
        cut.Find("[data-testid='document-status-zoom-out']").Click();

        raised.Should().Equal(110, 90);
        cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("100%");
    }

    [Fact]
    public void StatusBar_ZoomButtons_DisableAtTheLimits()
    {
        var atMin = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.ZoomPercent, 50).Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, _ => { })));
        atMin.Find("[data-testid='document-status-zoom-out']").HasAttribute("disabled").Should().BeTrue();
        atMin.Find("[data-testid='document-status-zoom-in']").HasAttribute("disabled").Should().BeFalse();

        var atMax = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.ZoomPercent, 200).Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, _ => { })));
        atMax.Find("[data-testid='document-status-zoom-in']").HasAttribute("disabled").Should().BeTrue();
        atMax.Find("[data-testid='document-status-zoom-out']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void StatusBar_ZoomStepNeverLeavesTheRange()
    {
        var raised = new List<int>();
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.ZoomPercent, 195)
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, raised.Add)));

        cut.Find("[data-testid='document-status-zoom-in']").Click();

        raised.Should().Equal(200);
    }

    [Fact]
    public void StatusBar_WithoutZoomHandler_HasNoZoomButtons_ButKeepsTheLabel()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.ZoomLabel, "75%"));

        cut.FindAll("[data-testid='document-status-zoom-in']").Should().BeEmpty();
        cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("75%");
    }

    [Fact]
    public void StatusBar_Fullscreen_RunsTheExistingCommand_AndReflectsThePressedState()
    {
        var calls = 0;
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.IsFullscreen, true)
            .Add(x => x.OnToggleFullscreen, EventCallback.Factory.Create(this, () => calls++)));

        var button = cut.Find("[data-testid='document-status-fullscreen']");
        button.GetAttribute("aria-pressed").Should().Be("true");
        button.Click();

        calls.Should().Be(1);
    }

    [Fact]
    public void StatusBar_LastSaved_IsShownOnlyWhenTheHeaderDoesNotOwnTheSaveState()
    {
        var saved = DateTimeOffset.UtcNow;
        var withHeader = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.LastSavedAt, saved).Add(x => x.ShowLastSaved, false));
        var withoutHeader = Render<TmDocumentEditorStatusBar>(p => p.Add(x => x.LastSavedAt, saved).Add(x => x.ShowLastSaved, true));

        withHeader.FindAll("[data-testid='document-last-saved']").Should().BeEmpty();
        withoutHeader.FindAll("[data-testid='document-last-saved']").Should().HaveCount(1);
    }

    [Fact]
    public void StatusBar_OnlyTheSaveGroupIsALiveRegion_NotTheWholeBar()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, _ => { })));

        var footer = cut.Find("[data-testid='document-status-bar']");
        footer.HasAttribute("role").Should().BeFalse("controls and counts inside one status region would be announced as a whole");
        footer.HasAttribute("aria-live").Should().BeFalse();
        footer.GetAttribute("aria-label").Should().Be("Document status");

        var save = cut.Find(".tm-document-editor__status-bar-group--save");
        save.GetAttribute("role").Should().Be("status");
        save.GetAttribute("aria-live").Should().Be("polite");
        cut.FindAll("[role='status'] button").Should().BeEmpty("no control lives inside a live region");
    }
}
