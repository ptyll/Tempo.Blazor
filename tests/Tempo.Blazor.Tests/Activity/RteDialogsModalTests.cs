using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// FZE-10: every rich-text-editor dialog (link, image, table, video, find/replace) renders on the
/// core <see cref="TmModal"/> primitive — the browser top layer, <c>role="dialog"</c>, a title the
/// dialog is named by (<c>aria-labelledby</c>), Escape and the shared focus trap — instead of its
/// own fixed <c>z-index</c> overlay (docs/overlays.md rule 9). Form behaviour is pinned alongside
/// so the migration preserves the insertion contracts.
/// </summary>
public class RteDialogsModalTests : LocalizationTestBase
{
    // ── Focus returns to the editor (the restore contract) ────────────────────

    [Fact]
    public void LinkDialog_FocusReturnsToTheEditor_RestoreTargetReachesTheTrap()
    {
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.RestoreFocusTargetId, "rte-editor-1"));

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be("rte-editor-1",
            "the shared focus trap restores focus to the host editor's content surface on every close path");
    }

    [Fact]
    public void ImageDialog_FocusReturnsToTheEditor_RestoreTargetReachesTheTrap()
    {
        var cut = Render<ImageDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.RestoreFocusTargetId, "rte-editor-1"));

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be("rte-editor-1");
    }

    [Fact]
    public void TableDialog_FocusReturnsToTheEditor_RestoreTargetReachesTheTrap()
    {
        var cut = Render<TableDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.RestoreFocusTargetId, "rte-editor-1"));

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be("rte-editor-1");
    }

    [Fact]
    public void VideoDialog_FocusReturnsToTheEditor_RestoreTargetReachesTheTrap()
    {
        var cut = Render<VideoDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.RestoreFocusTargetId, "rte-editor-1"));

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be("rte-editor-1");
    }

    [Fact]
    public void FindReplaceDialog_FocusReturnsToTheEditorSelection_RestoreTargetReachesTheTrap()
    {
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.RestoreFocusTargetId, "rte-editor-1"));

        cut.Find(".tm-focus-scope").GetAttribute("data-restore-target").Should().Be("rte-editor-1",
            "on close focus must land back in the editor, where the host replays the saved caret/selection");
    }

    // ── Link dialog ───────────────────────────────────────────────────────────

    [Fact]
    public void LinkDialog_RendersInTmModal_WithDialogRoleAndTitle()
    {
        var cut = Render<LinkDialog>(p => p.Add(x => x.IsVisible, true));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.ClassList.Should().Contain("tm-rte-link-dialog",
            "the consumer styling hook stays on the modal overlay root");

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(labelledBy);
        title.TextContent.Trim().Should().Be("Insert Link");

        cut.FindAll(".tm-rte-link-backdrop").Should().BeEmpty(
            "the fixed z-index backdrop is replaced by the TmModal overlay");
    }

    [Fact]
    public async Task LinkDialog_Escape_Cancels()
    {
        var cancelled = false;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.FindComponent<TmFocusScope>(); // the modal owns the trap that delivers Escape

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        // Awaited: the escape path drives the host cancel/insert pipeline synchronously in bUnit.
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cancelled.Should().BeTrue("Escape must close the dialog through OnCancel");
    }

    [Fact]
    public void LinkDialog_BackdropClick_Cancels_OnlyWhenEnabled()
    {
        var cancelled = false;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.Find(".tm-modal-overlay").Click();
        cancelled.Should().BeTrue("the default CloseOnBackdropClick=true dismisses on an overlay click");

        cancelled = false;
        cut.Render(p => p.Add(x => x.CloseOnBackdropClick, false));
        cut.Find(".tm-modal-overlay").Click();
        cancelled.Should().BeFalse("CloseOnBackdropClick=false must keep the dialog open");
    }

    [Fact]
    public void LinkDialog_CloseButton_Cancels()
    {
        var cancelled = false;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.Find(".tm-modal-close").Click();
        cancelled.Should().BeTrue();
    }

    [Fact]
    public void LinkDialog_UrlInput_TakesInitialFocus()
    {
        var cut = Render<LinkDialog>(p => p.Add(x => x.IsVisible, true));

        var urlInput = cut.Find(".tm-rte-link-url-input");
        var initialFocus = cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus");
        initialFocus.Should().NotBeNullOrEmpty("the first form field receives the initial focus");
        initialFocus.Should().Be(urlInput.GetAttribute("id"),
            "data-initial-focus points at the URL input's id");
    }

    [Fact]
    public void LinkDialog_Insert_DisabledUntilUrlIsValid_And_ConfirmsWithUrlAndText()
    {
        (string Url, string Text) confirmed = default;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Text)>(this, v => confirmed = v)));

        var insert = cut.Find(".tm-rte-btn-insert");
        insert.IsDisabled().Should().BeTrue("an empty URL cannot be inserted");

        cut.Find(".tm-rte-link-url-input").Input("https://example.com");
        cut.Find(".tm-rte-btn-insert").IsDisabled().Should().BeFalse();

        cut.Find(".tm-rte-link-text-input").Input("Docs");
        ((IHtmlFormElement)cut.Find("form")).Submit();

        confirmed.Url.Should().Be("https://example.com");
        confirmed.Text.Should().Be("Docs");
    }

    [Fact]
    public void LinkDialog_Insert_WithoutText_FallsBackToTheUrl()
    {
        (string Url, string Text) confirmed = default;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Text)>(this, v => confirmed = v)));

        cut.Find(".tm-rte-link-url-input").Input("https://example.com");
        ((IHtmlFormElement)cut.Find("form")).Submit();

        confirmed.Text.Should().Be("https://example.com");
    }

    [Fact]
    public void LinkDialog_FieldsLiveInAForm_TheInsertButtonSubmitsIt_CancelDoesNot()
    {
        var confirms = 0;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Text)>(this, _ => confirms++)));

        var form = cut.Find("form");
        var urlInput = cut.Find(".tm-rte-link-url-input");
        urlInput.GetAttribute("id").Should().NotBeNullOrEmpty();
        form.QuerySelector($"#{urlInput.GetAttribute("id")}").Should().NotBeNull("the fields are form content");

        var insert = cut.Find(".tm-rte-btn-insert");
        insert.GetAttribute("type").Should().Be("submit", "the primary footer button is the form's submit");
        insert.GetAttribute("form").Should().Be(form.GetAttribute("id"), "the submit button is owned by the form via its id");

        cut.Find(".tm-rte-btn-cancel").GetAttribute("type").Should().Be("button",
            "Cancel must never submit the form");

        cut.Find(".tm-rte-link-url-input").Input("https://example.com");
        ((IHtmlFormElement)cut.Find("form")).Submit(); // the browser's implicit submission path

        confirms.Should().Be(1, "submitting the form runs the confirm handler exactly once");
    }

    [Fact]
    public void LinkDialog_SubmitWithInvalidUrl_ShowsError_AndDoesNotConfirm()
    {
        var confirms = 0;
        var cut = Render<LinkDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Text)>(this, _ => confirms++)));

        cut.Find(".tm-rte-link-url-input").Input("not a url");
        cut.FindAll(".tm-rte-link-error").Should().BeEmpty();

        // Force the submit handler with an invalid URL (the button is disabled, but Enter in a
        // field still dispatches the submit event in a novalidate form) — the handler must
        // re-validate and show the error instead of confirming.
        ((IHtmlFormElement)cut.Find("form")).Submit();

        cut.Find(".tm-rte-link-error").TextContent.Trim().Should().Be("Please enter a valid URL");
        confirms.Should().Be(0, "an invalid URL is re-validated away before OnConfirm");
    }

    [Fact]
    public void LinkDialog_UrlPlaceholder_IsLocalized()
    {
        var cut = Render<LinkDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-rte-link-url-input").GetAttribute("placeholder")
            .Should().Be("https://example.com", "the placeholder is a localized resource, not an inline literal");
    }

    // ── Image dialog ──────────────────────────────────────────────────────────

    [Fact]
    public void ImageDialog_RendersInTmModal_WithDialogRoleAndTitle()
    {
        var cut = Render<ImageDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-rte-image-dialog");

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(labelledBy);
        title.TextContent.Trim().Should().Be("Insert Image");

        cut.FindAll(".tm-rte-image-backdrop").Should().BeEmpty();
    }

    [Fact]
    public async Task ImageDialog_Escape_Cancels()
    {
        var cancelled = false;
        var cut = Render<ImageDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        // Awaited: the escape path drives the host cancel/insert pipeline synchronously in bUnit.
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void ImageDialog_UrlInput_TakesInitialFocus()
    {
        var cut = Render<ImageDialog>(p => p.Add(x => x.IsVisible, true));

        var urlInput = cut.Find(".tm-rte-image-url-input");
        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus")
            .Should().Be(urlInput.GetAttribute("id"));
    }

    [Fact]
    public void ImageDialog_Insert_DisabledUntilUrlIsValid_And_ConfirmsWithUrlAndAlt()
    {
        (string Url, string Alt) confirmed = default;
        var cut = Render<ImageDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Alt)>(this, v => confirmed = v)));

        cut.Find(".tm-rte-btn-insert").IsDisabled().Should().BeTrue();

        cut.Find(".tm-rte-image-url-input").Input("https://example.com/image.jpg");
        cut.Find(".tm-rte-image-alt-input").Input("A chart");
        ((IHtmlFormElement)cut.Find("form")).Submit();

        confirmed.Url.Should().Be("https://example.com/image.jpg");
        confirmed.Alt.Should().Be("A chart");
    }

    [Fact]
    public void ImageDialog_FieldsLiveInAForm_TheInsertButtonSubmitsIt()
    {
        var confirms = 0;
        var cut = Render<ImageDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Alt)>(this, _ => confirms++)));

        var form = cut.Find("form");
        var insert = cut.Find(".tm-rte-btn-insert");
        insert.GetAttribute("type").Should().Be("submit");
        insert.GetAttribute("form").Should().Be(form.GetAttribute("id"));
        cut.Find(".tm-rte-btn-cancel").GetAttribute("type").Should().Be("button");

        cut.Find(".tm-rte-image-url-input").Input("https://example.com/image.jpg");
        ((IHtmlFormElement)cut.Find("form")).Submit();
        confirms.Should().Be(1, "submitting the form runs the confirm handler exactly once");
    }

    [Fact]
    public void ImageDialog_UrlPlaceholder_IsLocalized()
    {
        var cut = Render<ImageDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-rte-image-url-input").GetAttribute("placeholder")
            .Should().Be("https://example.com/image.jpg", "the placeholder is a localized resource, not an inline literal");
    }

    // ── Table dialog ──────────────────────────────────────────────────────────

    [Fact]
    public void TableDialog_RendersInTmModal_WithDialogRoleAndTitle()
    {
        var cut = Render<TableDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-rte-table-dialog");

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(labelledBy);
        title.TextContent.Trim().Should().Be("Insert Table");

        cut.FindAll(".tm-rte-table-backdrop").Should().BeEmpty();
    }

    [Fact]
    public async Task TableDialog_Escape_Cancels()
    {
        var cancelled = false;
        var cut = Render<TableDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        // Awaited: the escape path drives the host cancel/insert pipeline synchronously in bUnit.
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void TableDialog_RowsInput_TakesInitialFocus()
    {
        var cut = Render<TableDialog>(p => p.Add(x => x.IsVisible, true));

        var rows = cut.Find(".tm-rte-table-rows-input");
        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus")
            .Should().Be(rows.GetAttribute("id"));
    }

    [Fact]
    public void TableDialog_GridClick_SetsSize_And_ConfirmsRowsAndCols()
    {
        (int Rows, int Cols) confirmed = default;
        var cut = Render<TableDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(int Rows, int Cols)>(this, v => confirmed = v)));

        // The grid is 8 rows × 6 cols; cell (2, 4) sits in row 2, col 4.
        var cell = cut.FindAll(".tm-rte-table-grid-cell")[6 + 3]; // row 2 (index 6), col 4 (index +3)
        cell.Click();

        cut.Find(".tm-rte-table-size-label").TextContent.Trim().Should().Contain("2").And.Contain("4");
        ((IHtmlFormElement)cut.Find("form")).Submit();

        confirmed.Rows.Should().Be(2);
        confirmed.Cols.Should().Be(4);
    }

    [Fact]
    public void TableDialog_FieldsLiveInAForm_TheInsertButtonSubmitsIt()
    {
        var confirms = 0;
        var cut = Render<TableDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(int Rows, int Cols)>(this, _ => confirms++)));

        var form = cut.Find("form");
        var insert = cut.Find(".tm-rte-btn-insert");
        insert.GetAttribute("type").Should().Be("submit");
        insert.GetAttribute("form").Should().Be(form.GetAttribute("id"));
        cut.Find(".tm-rte-btn-cancel").GetAttribute("type").Should().Be("button");

        ((IHtmlFormElement)cut.Find("form")).Submit(); // defaults 3×3 are valid
        confirms.Should().Be(1, "submitting the form runs the confirm handler exactly once");
    }

    [Fact]
    public void TableDialog_PreviewIsPointerOnly_GridAndLabelHiddenFromTheA11yTree_SizeAnnouncedPolitely()
    {
        var cut = Render<TableDialog>(p => p.Add(x => x.IsVisible, true));

        var label = cut.Find(".tm-rte-table-preview-label");
        label.TagName.Should().Be("DIV", "the preview caption is not a form label — it labels nothing");
        label.GetAttribute("aria-hidden").Should().Be("true");

        cut.Find(".tm-rte-table-grid").GetAttribute("aria-hidden").Should().Be("true",
            "the grid is a mouse-only sizing aid");

        cut.Find(".tm-rte-table-size-label").GetAttribute("aria-live").Should().Be("polite",
            "the chosen size is announced without interrupting");
    }

    // ── Video dialog ──────────────────────────────────────────────────────────

    [Fact]
    public void VideoDialog_RendersInTmModal_WithDialogRoleAndTitle()
    {
        var cut = Render<VideoDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-rte-video-dialog");

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(labelledBy);
        title.TextContent.Trim().Should().Be("Insert Video");

        cut.FindAll(".tm-rte-video-backdrop").Should().BeEmpty();
    }

    [Fact]
    public async Task VideoDialog_Escape_Cancels()
    {
        var cancelled = false;
        var cut = Render<VideoDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        // Awaited: the escape path drives the host cancel/insert pipeline synchronously in bUnit.
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void VideoDialog_UrlInput_TakesInitialFocus()
    {
        var cut = Render<VideoDialog>(p => p.Add(x => x.IsVisible, true));

        var urlInput = cut.Find(".tm-rte-video-url-input");
        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus")
            .Should().Be(urlInput.GetAttribute("id"));
    }

    [Fact]
    public void VideoDialog_Insert_EmptyTitle_ConfirmsWithLocalizedDefault()
    {
        (string Url, string Title) confirmed = default;
        var cut = Render<VideoDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Title)>(this, v => confirmed = v)));

        cut.Find(".tm-rte-video-url-input").Input("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        ((IHtmlFormElement)cut.Find("form")).Submit();

        confirmed.Url.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        confirmed.Title.Should().Be("Video",
            "the fallback title is a localized user-facing string, never a hard-coded literal");
    }

    [Fact]
    public void VideoDialog_Insert_DisabledForNonVideoUrl()
    {
        var cut = Render<VideoDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-rte-video-url-input").Input("not a url");
        cut.Find(".tm-rte-btn-insert").IsDisabled().Should().BeTrue();
    }

    [Fact]
    public void VideoDialog_FieldsLiveInAForm_TheInsertButtonSubmitsIt()
    {
        var confirms = 0;
        var cut = Render<VideoDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<(string Url, string Title)>(this, _ => confirms++)));

        var form = cut.Find("form");
        var insert = cut.Find(".tm-rte-btn-insert");
        insert.GetAttribute("type").Should().Be("submit");
        insert.GetAttribute("form").Should().Be(form.GetAttribute("id"));
        cut.Find(".tm-rte-btn-cancel").GetAttribute("type").Should().Be("button");

        cut.Find(".tm-rte-video-url-input").Input("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        ((IHtmlFormElement)cut.Find("form")).Submit();
        confirms.Should().Be(1, "submitting the form runs the confirm handler exactly once");
    }

    [Fact]
    public void VideoDialog_UrlPlaceholder_IsLocalized()
    {
        var cut = Render<VideoDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-rte-video-url-input").GetAttribute("placeholder")
            .Should().Be("https://youtube.com/watch?v=...", "the placeholder is a localized resource, not an inline literal");
    }

    // ── Find/replace dialog ───────────────────────────────────────────────────

    [Fact]
    public void FindReplaceDialog_RendersInTmModal_WithDialogRoleAndTitle()
    {
        var cut = Render<FindReplaceDialog>(p => p.Add(x => x.IsVisible, true));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-rte-findreplace-dialog");

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(labelledBy);
        title.TextContent.Trim().Should().Be("Find & Replace");

        cut.FindAll(".tm-rte-findreplace-backdrop").Should().BeEmpty();
    }

    [Fact]
    public async Task FindReplaceDialog_Escape_Cancels()
    {
        var cancelled = false;
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        // Awaited: the escape path runs the host's cancel pipeline synchronously in bUnit.
        await cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync());

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void FindReplaceDialog_FindInput_TakesInitialFocus()
    {
        var cut = Render<FindReplaceDialog>(p => p.Add(x => x.IsVisible, true));

        var findInput = cut.Find(".tm-rte-findreplace-find-input");
        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus")
            .Should().Be(findInput.GetAttribute("id"));
    }

    [Fact]
    public void FindReplaceDialog_Replace_ConfirmsFindAndReplaceText_AndShowsTheRealCount()
    {
        (string Find, string Replace) replaced = default;
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnReplace, new Func<(string Find, string Replace), Task<int>>(args =>
            {
                replaced = args;
                return Task.FromResult(3); // the real count from tmRichEditor.findAndReplace
            })));

        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-findreplace-replace-input").Input("beat");
        ((IHtmlFormElement)cut.Find("form")).Submit();

        replaced.Find.Should().Be("tempo");
        replaced.Replace.Should().Be("beat");
        cut.Find(".tm-rte-findreplace-result").TextContent.Trim().Should().Be("3 occurrence(s) replaced",
            "the dialog reports the REAL count the host measured, never a hard-coded number");
    }

    [Fact]
    public void FindReplaceDialog_ReplaceAll_ConfirmsFindAndReplaceText_AndShowsTheRealCount()
    {
        (string Find, string Replace) replaced = default;
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnReplaceAll, new Func<(string Find, string Replace), Task<int>>(args =>
            {
                replaced = args;
                return Task.FromResult(7);
            })));

        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-findreplace-replace-input").Input("beat");
        cut.Find(".tm-rte-btn-replaceall").Click();

        replaced.Find.Should().Be("tempo");
        replaced.Replace.Should().Be("beat");
        cut.Find(".tm-rte-findreplace-result").TextContent.Trim().Should().Be("7 occurrence(s) replaced");
    }

    [Fact]
    public void FindReplaceDialog_ZeroMatches_ShowsLocalizedNoMatches()
    {
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnReplaceAll, new Func<(string Find, string Replace), Task<int>>(_ => Task.FromResult(0))));

        cut.FindAll(".tm-rte-findreplace-result").Should().BeEmpty("nothing ran yet, so no verdict yet");

        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-btn-replaceall").Click();

        cut.Find(".tm-rte-findreplace-result").TextContent.Trim().Should().Be("No matches found",
            "0 must be reported as no-matches, not as a successful replace of 0");
    }

    [Fact]
    public void FindReplaceDialog_IsTopAnchored_AndIgnoresOverlayClicks()
    {
        var cancelled = false;
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.Find(".tm-modal-container").ClassList.Should().Contain("tm-modal--top",
            "a find bar belongs at the top of the viewport, over the editor's headline");
        cut.Find(".tm-modal-overlay").Click();
        cancelled.Should().BeFalse(
            "a stray click outside the find bar (the editor below it) must not dismiss it");
    }

    [Fact]
    public void FindReplaceDialog_Footer_HasGhostCancel_SecondaryReplaceAsSubmit_PrimaryReplaceAll()
    {
        var cut = Render<FindReplaceDialog>(p => p.Add(x => x.IsVisible, true));

        var footer = cut.Find(".tm-modal-footer");
        var buttons = footer.QuerySelectorAll("button");
        buttons.Select(b => b.ClassName).Should().Contain(c => c.Contains("tm-btn-ghost"), "Cancel is Ghost");
        buttons.Select(b => b.ClassName).Should().Contain(c => c.Contains("tm-btn-secondary"), "Replace is Secondary");
        buttons.Select(b => b.ClassName).Should().Contain(c => c.Contains("tm-btn-primary"), "Replace All is the only Primary");

        var submit = footer.QuerySelector(".tm-rte-btn-replace")!;
        submit.GetAttribute("type").Should().Be("submit", "Enter in a field runs Replace (single), never Replace All");
        submit.GetAttribute("form").Should().Be(cut.Find("form").GetAttribute("id"), "the submit button submits the form by id");

        footer.QuerySelector(".tm-rte-btn-replaceall")!.GetAttribute("type").Should().Be("button",
            "Replace All is click-only so Enter cannot trigger the destructive bulk action");
    }

    [Fact]
    public void FindReplaceDialog_SubmitViaForm_ReplacesAndReportsCount()
    {
        var replaces = 0;
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnReplace, new Func<(string Find, string Replace), Task<int>>(_ =>
            {
                replaces++;
                return Task.FromResult(1);
            })));

        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-findreplace-replace-input").Input("beat");

        // Submit the form the way the browser's implicit submission does — the type=submit
        // footer button is the form's default button, owned by the form's id.
        ((IHtmlFormElement)cut.Find("form")).Submit();

        replaces.Should().Be(1, "the form submit runs the Replace handler exactly once");
    }

    [Fact]
    public void FindReplaceDialog_CloseButton_Cancels_AndClearsState()
    {
        var cancelled = false;
        var cut = Render<FindReplaceDialog>(p => p
            .Add(x => x.IsVisible, true)
            .Add(x => x.OnCancel, () => cancelled = true)
            .Add(x => x.OnReplaceAll, new Func<(string Find, string Replace), Task<int>>(_ => Task.FromResult(2))));

        cut.Find(".tm-rte-findreplace-find-input").Input("tempo");
        cut.Find(".tm-rte-findreplace-replace-input").Input("beat");
        cut.Find(".tm-rte-btn-replaceall").Click();
        cut.Find(".tm-rte-findreplace-result").Should().NotBeNull("the replace count is announced after a replace");

        cut.Find(".tm-modal-close").Click();

        cancelled.Should().BeTrue();
        cut.FindAll(".tm-rte-findreplace-result").Should().BeEmpty("the replace count resets on close");
        cut.Find(".tm-rte-findreplace-find-input").GetAttribute("value").Should().BeEmpty(
            "the dialog's inputs reset when the dialog is closed");
    }
}
