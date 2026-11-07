using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 4 (P3, NO1): <see cref="NotionCommentPresentation"/> — <c>Dialog</c> renders the block /
/// text comment panels through the core <see cref="TmModal"/> (titled, footer "Close", Escape →
/// OnClosed); <c>Auto</c> keeps the anchored popover on desktop and a sheet on mobile. The popover
/// declares no own backdrop, z-index or false aria-modal.
/// </summary>
public sealed class TmNotionCommentDialogsTests : LocalizationTestBase
{
    public TmNotionCommentDialogsTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Back"] = "Back",
            ["Tm_Delete"] = "Delete",
            ["Tm_Cancel"] = "Cancel",
            ["TmNotionComment_BlockDialogTitle"] = "Block comment ({0})",
            ["TmNotionComment_TextDialogTitle"] = "Text comment",
            ["TmNotionComment_ShowFullQuote"] = "Show full quote",
            ["TmNotionBlockComment_PanelLabel"] = "Block comments",
            ["TmNotionBlockComment_Title"] = "Comments",
            ["TmNotionBlockComment_NewThread"] = "New thread",
            ["TmNotionBlockComment_ThreadDetail"] = "Thread",
            ["TmNotionBlockComment_ThreadList"] = "Threads",
            ["TmNotionBlockComment_Thread"] = "Thread",
            ["TmNotionBlockComment_ThreadActions"] = "Actions",
            ["TmNotionBlockComment_EntryActions"] = "Entry actions",
            ["TmNotionBlockComment_Resolve"] = "Resolve",
            ["TmNotionBlockComment_Unresolve"] = "Re-open",
            ["TmNotionBlockComment_Resolved"] = "Resolved",
            ["TmNotionBlockComment_Edited"] = "edited",
            ["TmNotionBlockComment_Edit"] = "Edit",
            ["TmNotionBlockComment_Delete"] = "Delete",
            ["TmNotionBlockComment_DeleteConfirm"] = "Delete?",
            ["TmNotionBlockComment_EditLabel"] = "Edit comment",
            ["TmNotionBlockComment_Save"] = "Save",
            ["TmNotionBlockComment_ReplyToThis"] = "Reply to this",
            ["TmNotionBlockComment_ReplyPlaceholder"] = "Reply…",
            ["TmNotionBlockComment_NewPlaceholder"] = "New comment…",
            ["TmNotionBlockComment_ReplyLabel"] = "Reply",
            ["TmNotionBlockComment_ReplyHint"] = "Ctrl+Enter",
            ["TmNotionBlockComment_Reply"] = "Reply",
            ["TmNotionBlockComment_Comment"] = "Comment",
            ["TmNotionBlockComment_MarkAsRead"] = "Mark as read",
            ["TmNotionBlockComment_Watch"] = "Watch",
            ["TmNotionBlockComment_Unwatch"] = "Unwatch",
            ["TmNotionBlockComment_RepliesCount"] = "{0} replies",
            ["TmNotionBlockComment_NoComments"] = "No comments",
            ["TmNotionBlockComment_Loading"] = "Loading",
            ["TmNotionTextComment_PanelLabel"] = "Text comment",
            ["TmNotionTextComment_HighlightedText"] = "Highlighted text",
            ["TmNotionTextComment_Thread"] = "Thread",
            ["TmNotionTextComment_Resolve"] = "Resolve",
            ["TmNotionTextComment_Unresolve"] = "Re-open",
            ["TmNotionTextComment_Resolved"] = "Resolved",
            ["TmNotionTextComment_Edit"] = "Edit",
            ["TmNotionTextComment_Delete"] = "Delete",
            ["TmNotionTextComment_DeleteConfirm"] = "Delete?",
            ["TmNotionTextComment_EditLabel"] = "Edit comment",
            ["TmNotionTextComment_Save"] = "Save",
            ["TmNotionTextComment_ReplyToThis"] = "Reply to this",
            ["TmNotionTextComment_ReplyPlaceholder"] = "Reply…",
            ["TmNotionTextComment_NewPlaceholder"] = "New comment…",
            ["TmNotionTextComment_ReplyLabel"] = "Reply",
            ["TmNotionTextComment_ReplyHint"] = "Ctrl+Enter",
            ["TmNotionTextComment_Reply"] = "Reply",
            ["TmNotionTextComment_Comment"] = "Comment",
            ["TmNotionTextComment_Watch"] = "Watch",
            ["TmNotionTextComment_Unwatch"] = "Unwatch",
            ["TmNotionTextComment_NoComments"] = "No comments",
            ["TmNotionTextComment_Loading"] = "Loading",
            ["TmCommentReactions_Label"] = "Reactions",
            ["TmCommentReactions_Add"] = "Add reaction",
            ["TmCommentReactions_You"] = "You",
            ["TmCommentReactions_More"] = "+{0} more",
        });
    }

    [Fact]
    public void BlockComment_Dialog_RendersTitledModalWithCloseFooter()
    {
        var provider = new BlockFakeCommentProvider();
        SeedTwoThreads(provider);
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog);

        var modal = cut.FindComponent<TmModal>();
        modal.Instance.Title.Should().Be("Block comment (2)");
        cut.Find(".tm-modal-title").TextContent.Should().Be("Block comment (2)");
        cut.Find(".tm-modal-footer").TextContent.Should().Contain("Close");
        cut.Find(".tm-nbcp").Should().NotBeNull();
    }

    [Fact]
    public void BlockComment_Dialog_TwoOrMoreThreads_RendersMasterDetailSplit()
    {
        var provider = new BlockFakeCommentProvider();
        SeedTwoThreads(provider);
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog);

        cut.Find(".tm-nbcp--split").Should().NotBeNull();
        cut.Find(".tm-nbcp__thread-list").Should().NotBeNull();

        // Selecting a thread keeps the list visible (master/detail) and drops the back affordance.
        cut.Find(".tm-nbcp__thread-card__open").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-nbcp__thread-list").Should().NotBeNull());
        cut.FindAll(".tm-nbcp__back-btn").Should().BeEmpty();
    }

    [Fact]
    public void BlockComment_DialogMobile_OneColumnWithBackNavigation()
    {
        var provider = new BlockFakeCommentProvider();
        SeedTwoThreads(provider);
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog, TmLayoutMode.Mobile);

        cut.FindAll(".tm-nbcp--split").Should().BeEmpty();
        cut.Find(".tm-nbcp__thread-card__open").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-nbcp__back-btn").Should().NotBeNull());
    }

    [Fact]
    public void BlockComment_Dialog_Escape_RaisesOnClosed()
    {
        var provider = new BlockFakeCommentProvider();
        var closed = 0;
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog, onClosed: () => closed++);

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        closed.Should().Be(1);
    }

    [Fact]
    public void BlockComment_Dialog_NoThreads_StillRendersTheComposer()
    {
        // A block without threads auto-enters new-thread mode — the dialog must not open an
        // empty body with only a Close footer (UX review of the phase-4 mobile sheet capture).
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog);

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Should().NotBeNull();
        cut.Find(".tm-modal-footer").TextContent.Should().Contain("Close");
    }

    [Fact]
    public void BlockComment_NewThread_WithZeroThreads_HidesTheBackButton()
    {
        // There is no thread list to go back to — the ← affordance would strand the user on an
        // empty sheet (UX review of the phase-4 mobile sheet capture).
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog);

        cut.FindAll(".tm-nbcp__back-btn").Should().BeEmpty();
    }

    [Fact]
    public void BlockComment_NewThread_WithThreads_KeepsTheBackButton()
    {
        // The anchored popover never splits (no master/detail), so a new thread on a block that
        // HAS threads keeps the ← back affordance — only the zero-thread sheet hides it.
        var provider = new BlockFakeCommentProvider();
        SeedTwoThreads(provider);
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Popover, TmLayoutMode.Desktop);

        cut.Find(".tm-nbcp__new-thread-btn").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-nbcp__back-btn").Should().NotBeNull());
    }

    [Fact]
    public void BlockComment_ComposerCss_CoarseHidesHintAndGrowsSubmit_DisabledUsesSurfaces()
    {
        // The coarse-pointer and disabled-send rules moved to the shared package stylesheet
        // (one copy for the three panels) in the phase-10 shrink.
        var css = StripComments(File.ReadAllText(Path.Combine(
            NotionUiPath(), "..", "..", "..", "wwwroot", "css", "tempo-blazor-notion-editor.css")));

        var coarse = System.Text.RegularExpressions.Regex.Match(css,
            @"@media \(pointer: coarse\)\s*\{(?<body>.*)\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        coarse.Success.Should().BeTrue();
        coarse.Groups["body"].Value.Should().Contain(".tm-nbcp__reply-hint");
        coarse.Groups["body"].Value.Should().Contain("display: none");
        coarse.Groups["body"].Value.Should().Contain("min-height: 44px");

        var disabled = System.Text.RegularExpressions.Regex.Match(css,
            @"tm-nbcp__reply-send:disabled[^{]*\{(?<body>[^}]*)\}");
        disabled.Success.Should().BeTrue();
        disabled.Groups["body"].Value.Should().Contain("var(--tm-bg-muted)");
        disabled.Groups["body"].Value.Should().Contain("var(--tm-text-disabled)");
        disabled.Groups["body"].Value.Should().Contain("opacity: 1");
    }

    [Fact]
    public void BlockComment_AutoDesktop_KeepsTheAnchoredPopover()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Auto, TmLayoutMode.Desktop);

        cut.Find(".tm-nbcp").Should().NotBeNull();
        // The popover rides the core TmOverlayPanel (top layer) — no false aria-modal, no own backdrop.
        cut.Find(".tm-nbcp").HasAttribute("aria-modal").Should().BeFalse();
        cut.FindAll(".tm-nbcp-backdrop").Should().BeEmpty();
        cut.FindComponents<TmModal>().Should().BeEmpty();
    }

    [Fact]
    public void BlockComment_AutoDesktop_PopoverIsAOverlayPanelDialogAnchoredAtTheBubble()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Auto, TmLayoutMode.Desktop);

        // The zero-size anchor pins the bubble position; the overlay panel owns the dialog role,
        // the accessible name, Escape/outside-click dismissal and the top-layer promotion.
        cut.Find(".tm-nbcp-anchor").Should().NotBeNull();
        var overlay = cut.FindComponent<TmOverlayPanel>();
        overlay.Instance.Role.Should().Be("dialog");
        overlay.Instance.AriaLabel.Should().Be("Block comments");
    }

    [Fact]
    public void BlockComment_Popover_DismissalRaisesOnClosed()
    {
        var provider = new BlockFakeCommentProvider();
        var closed = 0;
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Auto, TmLayoutMode.Desktop, onClosed: () => closed++);

        // overlay.js delivers Escape / outside click through NotifyDismissedAsync.
        cut.InvokeAsync(() => cut.FindComponent<TmOverlayPanel>().Instance.NotifyDismissedAsync("escape"))
            .GetAwaiter().GetResult();

        closed.Should().Be(1);
    }

    [Fact]
    public void BlockComment_Dialog_CardEmitsNoOwnDialogRoleOrName()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Dialog);

        // Inside the TmModal the TmFocusScope is the dialog: the card must not claim a second role.
        var card = cut.Find(".tm-nbcp");
        card.HasAttribute("role").Should().BeFalse();
        card.HasAttribute("aria-label").Should().BeFalse();
    }

    [Fact]
    public void BlockComment_AutoTablet_RendersTheModal()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Auto, TmLayoutMode.Tablet);

        cut.FindComponent<TmModal>().Should().NotBeNull();
        cut.Find(".tm-nbcp").Should().NotBeNull();
    }

    [Fact]
    public void BlockComment_AutoMobile_RendersAsSheet()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Auto, TmLayoutMode.Mobile);

        cut.Find(".tm-modal--sheet").Should().NotBeNull();
        cut.Find(".tm-nbcp").Should().NotBeNull();
    }

    [Fact]
    public void BlockComment_PopoverCss_DeclaresNoBackdropOrZIndex()
    {
        var css = StripComments(File.ReadAllText(Path.Combine(NotionUiPath(), "TmNotionBlockCommentPanel.razor.css")));
        css.Should().NotContain("tm-nbcp-backdrop");
        css.Should().NotContain("z-index");

        var razor = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Path.Combine(NotionUiPath(), "TmNotionBlockCommentPanel.razor")),
            @"(?s)@\*.*?\*@", string.Empty);
        razor.Should().NotContain("tm-nbcp-backdrop");
        razor.Should().NotContain("aria-modal");
    }

    private static string StripComments(string css)
        => System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));

    [Fact]
    public void TextComment_PanelCss_EmptyStateIsCentered_AndThreadPaddingUsesTokens()
    {
        var css = StripComments(File.ReadAllText(Path.Combine(NotionUiPath(), "TmNotionTextCommentPanel.razor.css")));

        var thread = System.Text.RegularExpressions.Regex.Match(css,
            @"\.tm-ntcp__thread\s*\{(?<body>[^}]*)\}");
        thread.Success.Should().BeTrue();
        thread.Groups["body"].Value.Should().Contain("padding: var(--tm-space-3) var(--tm-space-4)");

        var empty = System.Text.RegularExpressions.Regex.Match(css,
            @"\.tm-ntcp__status--empty\s*\{(?<body>[^}]*)\}");
        empty.Success.Should().BeTrue();
        empty.Groups["body"].Value.Should().Contain("justify-content: center");
    }

    [Fact]
    public void TextComment_Dialog_RendersTitledModalWithFullQuote()
    {
        var provider = new BlockFakeCommentProvider();
        var quote = string.Join(" ", Enumerable.Repeat("Highlighted passage with enough words to be clamped.", 12));
        var cut = RenderTextPanel(provider, NotionCommentPresentation.Dialog, quote);

        cut.FindComponent<TmModal>().Instance.Title.Should().Be("Text comment");
        var snippet = cut.Find(".tm-ntcp__highlight-snippet");
        snippet.TextContent.Should().Contain(quote[..40]);
        snippet.TextContent.Length.Should().BeGreaterThan(60,
            "the dialog shows the whole quote — the 60-char popover truncation does not apply");
    }

    [Fact]
    public void TextComment_Dialog_ShowFullQuoteToggle()
    {
        var provider = new BlockFakeCommentProvider();
        var quote = string.Join(" ", Enumerable.Repeat("Long quoted text that clamps after four lines in the dialog.", 14));
        var cut = RenderTextPanel(provider, NotionCommentPresentation.Dialog, quote);

        var toggle = cut.Find(".tm-ntcp__quote-toggle");
        toggle.TextContent.Should().Contain("Show full quote");
        toggle.Click();
        cut.Find(".tm-ntcp__highlight-preview--expanded").Should().NotBeNull();
    }

    [Fact]
    public void TextComment_AutoDesktop_KeepsTheAnchoredPopover()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderTextPanel(provider, NotionCommentPresentation.Auto, "Short quote", TmLayoutMode.Desktop);

        cut.Find(".tm-ntcp").Should().NotBeNull();
        cut.Find(".tm-ntcp").HasAttribute("aria-modal").Should().BeFalse();
        cut.FindAll(".tm-ntcp-backdrop").Should().BeEmpty();
        cut.Find(".tm-ntcp-anchor").Should().NotBeNull();
        cut.FindComponent<TmOverlayPanel>().Instance.Role.Should().Be("dialog");
    }

    [Fact]
    public void TextComment_AutoTablet_RendersTheModal()
    {
        var provider = new BlockFakeCommentProvider();
        var cut = RenderTextPanel(provider, NotionCommentPresentation.Auto, "Short quote", TmLayoutMode.Tablet);

        cut.FindComponent<TmModal>().Should().NotBeNull();
        cut.Find(".tm-ntcp").Should().NotBeNull();
    }

    [Fact]
    public void BlockComment_ThreadList_StillRendersReplySend()
    {
        var provider = new BlockFakeCommentProvider();
        SeedTwoThreads(provider);
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Popover, TmLayoutMode.Desktop);

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-nbcp__reply-send").Should().NotBeEmpty(
                "the open panel must expose the original send class even on a thread list"));
    }

    [Fact]
    public void TextComment_Popover_DismissalRaisesOnClosed()
    {
        var provider = new BlockFakeCommentProvider();
        var closed = 0;
        var cut = RenderTextPanel(provider, NotionCommentPresentation.Auto, "Short quote", TmLayoutMode.Desktop, onClosed: () => closed++);

        cut.InvokeAsync(() => cut.FindComponent<TmOverlayPanel>().Instance.NotifyDismissedAsync("outside"))
            .GetAwaiter().GetResult();

        closed.Should().Be(1);
    }

    [Fact]
    public void BlockComment_Popover_EscapeWithDraft_KeepsPanelOpen_OutsideStillCloses()
    {
        // Escape typed in the composer (the E2E closes a mention menu that way) must not discard
        // the unsent comment: the popover vetoes an Escape dismissal while a draft exists.
        var provider = new BlockFakeCommentProvider();
        var closed = 0;
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Popover, TmLayoutMode.Desktop, onClosed: () => closed++);

        cut.Find(".tm-nbcp__reply-input").Input("Hey @alice check this out");
        cut.WaitForAssertion(() => cut.Instance.HasUnsavedDraft.Should().BeTrue());

        var overlay = cut.FindComponent<TmOverlayPanel>().Instance;
        cut.InvokeAsync(() => overlay.NotifyDismissedAsync("escape")).GetAwaiter().GetResult();
        closed.Should().Be(0);
        cut.FindAll(".tm-nbcp__reply-send").Should().NotBeEmpty();

        cut.InvokeAsync(() => overlay.NotifyDismissedAsync("outside")).GetAwaiter().GetResult();
        closed.Should().Be(1);
    }

    [Fact]
    public void BlockComment_ThreadListComposer_ReportsNewCommentDraft()
    {
        var provider = new BlockFakeCommentProvider();
        SeedTwoThreads(provider);
        var cut = RenderBlockPanel(provider, NotionCommentPresentation.Popover, TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__thread-list").Should().NotBeEmpty());
        cut.Find(".tm-nbcp__reply-input").Input("draft on the list");
        cut.WaitForAssertion(() => cut.Instance.HasUnsavedDraft.Should().BeTrue());
    }

    // ── Hosts ────────────────────────────────────────────────────────────────

    private IRenderedComponent<TmNotionBlockCommentPanel> RenderBlockPanel(
        BlockFakeCommentProvider provider,
        NotionCommentPresentation presentation,
        TmLayoutMode mode = TmLayoutMode.Desktop,
        Action? onClosed = null)
        => Render<CascadingValue<NotionEditorContext>>(p => p
            .Add(x => x.Value, new NotionEditorContext
            {
                DataProvider = default!,
                BlockService = default!,
                CommentProvider = provider
            })
            .Add(p2 => p2.IsFixed, false)
            .AddChildContent<CascadingValue<TmLayoutContext>>(layout => layout
                .Add(p => p.Value, new TmLayoutContext(mode, mode))
                .Add(p => p.Name, TmLayoutScopes.Viewport)
                .AddChildContent<CascadingValue<TmLayoutContext>>(viewport => viewport
                    .Add(x => x.Value, new TmLayoutContext(mode, mode))
                    .AddChildContent<TmNotionBlockCommentPanel>(panel => panel
                        .Add(x => x.Visible, true)
                        .Add(x => x.BlockId, "block-1")
                        .Add(x => x.Presentation, presentation)
                        .Add(x => x.OnClosed, EventCallback.Factory.Create(this, () => onClosed?.Invoke()))))))
            .FindComponent<TmNotionBlockCommentPanel>();

    private IRenderedComponent<TmNotionTextCommentPanel> RenderTextPanel(
        BlockFakeCommentProvider provider,
        NotionCommentPresentation presentation,
        string highlightedText,
        TmLayoutMode mode = TmLayoutMode.Desktop,
        Action? onClosed = null)
        => Render<CascadingValue<NotionEditorContext>>(p => p
            .Add(x => x.Value, new NotionEditorContext
            {
                DataProvider = default!,
                BlockService = default!,
                CommentProvider = provider
            })
            .Add(p2 => p2.IsFixed, false)
            .AddChildContent<CascadingValue<TmLayoutContext>>(layout => layout
                .Add(p => p.Value, new TmLayoutContext(mode, mode))
                .Add(p => p.Name, TmLayoutScopes.Viewport)
                .AddChildContent<CascadingValue<TmLayoutContext>>(viewport => viewport
                    .Add(x => x.Value, new TmLayoutContext(mode, mode))
                    .AddChildContent<TmNotionTextCommentPanel>(panel => panel
                        .Add(x => x.Visible, true)
                        .Add(x => x.CommentId, string.Empty)
                        .Add(x => x.BlockId, "block-1")
                        .Add(x => x.HighlightedText, highlightedText)
                        .Add(x => x.Presentation, presentation)
                        .Add(x => x.OnClosed, EventCallback.Factory.Create(this, () => onClosed?.Invoke()))))))
            .FindComponent<TmNotionTextCommentPanel>();

    private static void SeedTwoThreads(BlockFakeCommentProvider provider)
    {
        for (var i = 1; i <= 2; i++)
        {
            provider.CreateThreadAsync(new TmCommentThread
            {
                Id = $"thread-{i}",
                EntityRef = new TmEntityRef { EntityType = "notion-block", EntityId = "block-1" },
                Entries = [new TmCommentEntry { Body = $"Thread {i} body", BodyFormat = TmCommentBodyFormat.PlainText }],
            }).GetAwaiter().GetResult();
        }
    }

    private static string NotionUiPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Tempo.Blazor.NotionEditor", "Components", "NotionEditor", "UI");
    }
}
