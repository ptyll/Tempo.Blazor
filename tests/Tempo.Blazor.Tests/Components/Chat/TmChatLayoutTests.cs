using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Layout &amp; mobile contract of <see cref="Tempo.Blazor.Components.Chat.TmChat"/> (FZE-8) across
/// the scoped <c>TmChat.razor.css</c> and the global fallback <c>wwwroot/css/components/_chat.css</c>:
/// <list type="number">
/// <item>The chat is usable as a flex CHILD — <c>.tm-chat</c> reads its minimum height through the
/// <c>--tm-chat-min-height</c> knob (fallback 300px, today's value; never declared by the library)
/// and <c>.tm-chat__messages</c> keeps <c>min-height: 0</c> so the column can shrink; the
/// <c>FillParent</c> parameter adds <c>.tm-chat--fill</c> (<c>flex: 1 1 auto; height: auto;
/// min-height: 0</c>).</item>
/// <item>The chat is its own size container (<c>tm-chat</c>, intrinsic fallback 360px for
/// shrink-to-fit hosts, <c>tm-chat--no-container</c> opt-out) — on a narrow chat the thread panel
/// overlays the FULL width (hiding the covered body + composer), its header and reply row stick
/// like the composer (<c>--tm-chat-composer-offset</c>, safe-area aware via
/// <c>--tm-chat-safe-area-bottom</c>).</item>
/// <item>Coarse pointers get 44px targets: the send button, the per-message actions, the thread
/// close, the emoji, the edit save/cancel pair and the reply badge grow to
/// <c>--tm-touch-target</c>; the action bar moves into the flow and the composer input steps up
/// to <c>--tm-font-size-md</c>. The desktop sizes stay untouched.</item>
/// </list>
/// The library declares no <c>--tm-chat-*</c> knob defaults (the FZE-3 rule, extended to the FZE-8
/// knobs): every default lives in the <c>var()</c> fallback so a host override on a wrapper or on
/// <c>:root</c> cascades.
/// </summary>
public sealed class TmChatLayoutTests : LocalizationTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly Regex ChatKnobDeclaration =
        new(@"--tm-chat-(min-height|composer-offset|keyboard-inset|composer-clearance)\s*:", RegexOptions.Compiled, Timeout);

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(parts).ToArray());

    private static string ChatRazorCss() =>
        ThemeCss.StripComments(File.ReadAllText(
            RepoFile("src", "Tempo.Blazor", "Components", "Chat", "TmChat.razor.css")));

    private static string FallbackCss() => ThemeCss.ComponentCss("_chat.css");

    private static string BundledCss() => ThemeCss.BundledCss();

    /// <summary>A parsed rule: its selector text, its declaration body and the innermost at-rule
    /// (<c>@media …</c> / <c>@container …</c>) it sits in — an empty string at the top level.</summary>
    private sealed record ParsedRule(string Selector, string Body, string AtRule);

    /// <summary>A tiny block parser: unlike a flat regex it keeps the at-rule CONTEXT, which the
    /// assertions below need (the same selector appears at the top level and inside a media or
    /// container block with different declarations).</summary>
    private static List<ParsedRule> ParseRules(string css)
    {
        var rules = new List<ParsedRule>();
        Collect(css, string.Empty, rules);
        return rules;

        static void Collect(string text, string atRule, List<ParsedRule> found)
        {
            var index = 0;
            while (index < text.Length)
            {
                var open = text.IndexOf('{', index);
                if (open < 0)
                {
                    return;
                }

                var header = ThemeCss.Normalise(text[index..open]);
                var close = MatchingBrace(text, open);
                var body = text[(open + 1)..close];
                if (header.StartsWith('@'))
                {
                    // Only the INNERMOST at-rule matters for these assertions (no rule of this
                    // stylesheet nests a media inside a container block).
                    Collect(body, header, found);
                }
                else
                {
                    found.Add(new ParsedRule(header, body, atRule));
                }

                index = close + 1;
            }
        }

        static int MatchingBrace(string text, int open)
        {
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            throw new InvalidDataException("Unbalanced braces in the stylesheet under test.");
        }
    }

    /// <summary>The value of <paramref name="property"/> in the rule that lists
    /// <paramref name="selector"/> (exact, comma-part match) inside
    /// <paramref name="atRule"/> — null when the context has no such rule or declaration.
    /// The LAST declaration wins, mirroring the cascade.</summary>
    private static string? PropIn(string css, string selector, string property, string? atRule = null)
    {
        string? value = null;
        foreach (var rule in ParseRules(css))
        {
            if (!string.Equals(rule.AtRule, atRule ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = rule.Selector.Split(',').Select(part => part.Trim());
            if (!parts.Contains(selector, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var declaration in rule.Body.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                if (string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
                {
                    value = ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }
        }

        return value;
    }

    private const string CoarsePointer = "@media (hover: none), (pointer: coarse)";
    private const string NarrowChat = "@container tm-chat (width < 640px)";

    // ── 1. The chat is a flexible flex child ─────────────────────────────────

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ChatRoot_MinHeightReadsTheKnobWithTodaysDefault(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat", "min-height").Should().Be(
            "var(--tm-chat-min-height, 300px)",
            "the hardcoded 300px became a knob: the fallback keeps today's look, a host sets 0 (or " +
            "any floor) on a wrapper or :root and it cascades");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ChatRoot_IsItsOwnSizeContainer(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat", "container-type").Should().Be(
            "inline-size", "the narrow-chat behaviors branch on the CHAT's width, not the viewport");
        PropIn(css, ".tm-chat", "container-name").Should().Be("tm-chat");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void MessagesScroller_MayShrinkToZero(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__messages", "min-height").Should().Be(
            "0", "the flex child needs min-height: 0 or the message column can never shrink");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void FillParentModifier_TurnsTheChatIntoAFlexChild(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat--fill", "flex").Should().Be("1 1 auto");
        PropIn(css, ".tm-chat--fill", "height").Should().Be("auto");
        PropIn(css, ".tm-chat--fill", "min-height").Should().Be(
            "var(--tm-chat-min-height, 0)",
            "FillParent hands the height decision to the host's flex layout, but through the " +
            "knob's ZERO fallback — a floor declared on a wrapper or :root keeps applying in " +
            "fill mode too");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ChatRoot_KeepsAShrinkToFitIntrinsicWidth(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat", "contain-intrinsic-inline-size").Should().Be(
            "auto 360px",
            "a size container forces a definite inline size — the intrinsic fallback keeps the " +
            "chat from collapsing to 0 inside a shrink-to-fit host (float, table cell, grid auto)");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void NoContainerModifier_OptsOutOfTheSizeContainer(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat--no-container", "container-type").Should().Be(
            "normal", "the opt-out stops the chat from being a size container at all");
        PropIn(css, ".tm-chat--no-container", "container-name").Should().Be(
            "none", "with no name no @container tm-chat rule can match the chat's descendants");
    }

    [Fact]
    public void LibraryStylesheets_NeverDeclareTheLayoutKnobs()
    {
        ChatKnobDeclaration.Matches(ChatRazorCss()).Should().BeEmpty(
            "TmChat.razor.css must only READ --tm-chat-min-height / --tm-chat-composer-offset / " +
            "--tm-chat-keyboard-inset / --tm-chat-composer-clearance — a declaration would shadow " +
            "a host override on a wrapper or :root");
        ChatKnobDeclaration.Matches(FallbackCss()).Should().BeEmpty(
            "_chat.css mirrors the scoped stylesheet");
        ChatKnobDeclaration.Matches(BundledCss()).Should().BeEmpty(
            "the shipped bundle must not declare the layout knobs either");
    }

    // ── 2b. The FZE-13 runtime keyboard inset ─────────────────────────────────

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void StickyComposer_BottomAddsTheRuntimeKeyboardInset(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__input-area", "bottom", NarrowChat).Should().Be(
            "calc(var(--tm-chat-composer-offset, 0px) + var(--tm-chat-keyboard-inset, 0px))",
            "the FZE-13 tracker writes the runtime knob into the root's inline style while the " +
            "keyboard is open — the sticky composer follows the visual viewport, not the layout one");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ThreadInputArea_BottomAddsTheRuntimeKeyboardInset(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__thread-input-area", "bottom", NarrowChat).Should().Be(
            "calc(var(--tm-chat-composer-offset, 0px) + var(--tm-chat-keyboard-inset, 0px))",
            "the overlay's reply row clears the keyboard exactly like the composer");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void KeyboardOpenMarker_DropsTheSafeAreaPadding(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat[data-tm-keyboard-open] .tm-chat__input-area", "padding-bottom", NarrowChat)
            .Should().Be("var(--tm-space-3)",
                "the keyboard covers the home indicator — the safe-area padding would double-pad");
        PropIn(css, ".tm-chat[data-tm-keyboard-open] .tm-chat__thread-input-area", "padding-bottom", NarrowChat)
            .Should().Be("var(--tm-space-3)");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void KeyboardOpenComposer_RestsOnTheLargerOffset(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat[data-tm-keyboard-open] .tm-chat__input-area", "bottom", NarrowChat).Should().Be(
            "max(var(--tm-chat-composer-offset, 0px), var(--tm-chat-keyboard-inset, 0px))",
            "while the keyboard is open it covers the host's bottom navigation — the composer rests " +
            "on the LARGER of the two offsets instead of their SUM (keyboard + nav would push it " +
            "above the visible viewport)");
        PropIn(css, ".tm-chat[data-tm-keyboard-open] .tm-chat__thread-input-area", "bottom", NarrowChat).Should().Be(
            "max(var(--tm-chat-composer-offset, 0px), var(--tm-chat-keyboard-inset, 0px))",
            "the overlay's reply row rests on the keyboard exactly like the composer");
        PropIn(css, ".tm-chat__input-area", "bottom", NarrowChat).Should().Be(
            "calc(var(--tm-chat-composer-offset, 0px) + var(--tm-chat-keyboard-inset, 0px))",
            "the keyboard-CLOSED state keeps the additive calc — the knob's 0px fallback makes it " +
            "collapse to the plain composer offset");
        PropIn(css, ".tm-chat__thread-input-area", "bottom", NarrowChat).Should().Be(
            "calc(var(--tm-chat-composer-offset, 0px) + var(--tm-chat-keyboard-inset, 0px))");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void KeyboardOpenMessages_ReserveTheKeyboardGap(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat[data-tm-keyboard-open] .tm-chat__messages", "padding-bottom", NarrowChat).Should().Be(
            "calc(var(--tm-space-4) + var(--tm-chat-composer-clearance, 0px))",
            "while the keyboard is open the stuck composer covers the list's bottom — the tracker " +
            "MEASURES the covered gap into --tm-chat-composer-clearance and the list reserves it " +
            "as scroll room, so the rescued last row can rest ABOVE the composer even when the " +
            "messages themselves do not overflow");
        PropIn(css, ".tm-chat[data-tm-keyboard-open] .tm-chat__messages", "scroll-padding-bottom", NarrowChat)
            .Should().Be("var(--tm-chat-composer-clearance, 0px)",
                "native scrollIntoView paths (deep links, focus moves) must keep rows clear of the " +
                "stuck composer while the keyboard is open");
        PropIn(css, ".tm-chat__messages", "padding").Should().Be(
            "var(--tm-space-4)", "the keyboard-CLOSED list keeps the plain uniform padding");
    }

    // ── 2. Narrow chat: sticky composer + full-width thread overlay ──────────

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ComposerArea_IsStickyOnlyOnANarrowChat(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__input-area", "position").Should().BeNull(
            "the desktop composer must keep today's (static) position — sticky is the narrow-chat " +
            "behavior only");
        PropIn(css, ".tm-chat__input-area", "position", NarrowChat).Should().Be(
            "sticky", "on a narrow chat the composer sticks above the host's bottom navigation");
        PropIn(css, ".tm-chat__input-area", "bottom", NarrowChat).Should().Be(
            "calc(var(--tm-chat-composer-offset, 0px) + var(--tm-chat-keyboard-inset, 0px))",
            "the host opts in by setting the knob to the height of its bottom navigation; the " +
            "FZE-13 runtime keyboard inset rides on top (0px while the tracker is off/closed)");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ComposerArea_KeepsTheSafeAreaInset(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__input-area", "padding-bottom").Should().BeNull(
            "the base composer padding stays plain — the safe-area padding is the narrow-chat " +
            "behavior (a desktop chat has no home indicator to clear)");
        PropIn(css, ".tm-chat__input-area", "padding-bottom", NarrowChat).Should().Be(
            "calc(var(--tm-space-3) + var(--tm-chat-safe-area-bottom, env(safe-area-inset-bottom, 0px)))",
            "the composer clears the home indicator on a narrow chat; --tm-chat-safe-area-bottom " +
            "is the host's opt-out when its own chrome already pads the safe area");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ThreadHeader_IsStickyOnANarrowChat(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__thread-header", "position", NarrowChat).Should().Be(
            "sticky", "the overlay's header must stay visible when the chat extends past the fold");
        PropIn(css, ".tm-chat__thread-header", "top", NarrowChat).Should().Be("0");
        PropIn(css, ".tm-chat__thread-header", "background", NarrowChat).Should().Be(
            "var(--tm-bg-surface)", "scrolled-under messages must not show through the header");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ThreadInputArea_IsStickyWithTheSafeAreaOnANarrowChat(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__thread-input-area", "position", NarrowChat).Should().Be(
            "sticky", "the panel's reply row rests below the fold exactly like the composer");
        PropIn(css, ".tm-chat__thread-input-area", "bottom", NarrowChat).Should().Be(
            "calc(var(--tm-chat-composer-offset, 0px) + var(--tm-chat-keyboard-inset, 0px))",
            "the SAME knobs the composer reads — both rows clear the host's bottom chrome and " +
            "the FZE-13 keyboard together");
        PropIn(css, ".tm-chat__thread-input-area", "padding-bottom", NarrowChat).Should().Be(
            "calc(var(--tm-space-3) + var(--tm-chat-safe-area-bottom, env(safe-area-inset-bottom, 0px)))");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ThreadOverlay_HidesTheCoveredDirectChildren(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        var hidden = PropIn(css, ".tm-chat:has(> .tm-chat__thread-panel) > .tm-chat__body", "visibility", NarrowChat);
        hidden.Should().Be("hidden",
            "with the full-width overlay open the covered body is dead weight — and a screen " +
            "reader would read the same messages twice (root list + panel)");

        var composerHidden = PropIn(css, ".tm-chat:has(> .tm-chat__thread-panel) > .tm-chat__input-area", "visibility", NarrowChat);
        composerHidden.Should().Be("hidden",
            "the overlay keeps its own reply input, so the covered composer is hidden too");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ThreadPanel_OverlaysTheFullWidthOnANarrowChat(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__thread-panel", "width").Should().Be(
            "min(360px, 85%)", "the desktop/tablet panel stays a side overlay");
        PropIn(css, ".tm-chat__thread-panel", "left", NarrowChat).Should().Be(
            "0", "on a narrow chat the panel covers the whole chat instead of squeezing the composer");
        PropIn(css, ".tm-chat__thread-panel", "width", NarrowChat).Should().Be("100%");
        PropIn(css, ".tm-chat__thread-panel", "border-left", NarrowChat).Should().Be(
            "none", "a full-width overlay has no inner edge to draw");
    }

    // ── 3. 44px targets on coarse pointers ───────────────────────────────────

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void SendButton_Keeps40pxOnFinePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__send-btn", "width").Should().Be(
            "40px", "the desktop default look must not regress");
        PropIn(css, ".tm-chat__send-btn", "height").Should().Be("40px");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void SendButton_ReachesTheTouchTargetOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__send-btn", "min-width", CoarsePointer).Should().Be(
            "var(--tm-touch-target)",
            "44px is a pointer-modality requirement (WCAG 2.5.8) — the desktop window stays 40px");
        PropIn(css, ".tm-chat__send-btn", "min-height", CoarsePointer).Should().Be(
            "var(--tm-touch-target)");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void MessageActions_ReachTheTouchTargetOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__action", "min-width", CoarsePointer).Should().Be(
            "var(--tm-touch-target)", "the per-message actions are 26px icons — unusable with a finger");
        PropIn(css, ".tm-chat__action", "min-height", CoarsePointer).Should().Be(
            "var(--tm-touch-target)");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void SmallControls_ReachTheTouchTargetOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        foreach (var selector in new[]
                 {
                     ".tm-chat__thread-close",
                     ".tm-chat__emoji",
                     ".tm-chat__edit-save",
                     ".tm-chat__edit-cancel",
                 })
        {
            PropIn(css, selector, "min-height", CoarsePointer).Should().Be(
                "var(--tm-touch-target)", $"{selector} is smaller than 44px on fine pointers");
            PropIn(css, selector, "min-width", CoarsePointer).Should().Be("var(--tm-touch-target)");
        }
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ThreadBadge_ReachesTheTouchTargetOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__thread-badge", "min-height", CoarsePointer).Should().Be(
            "var(--tm-touch-target)", "the reply-count pill is a text button — a finger needs 44px");
        // Deliberately NO min-width: the pill is a text label, forcing a 44px WIDTH would add a
        // giant transparent hit area beside the text.
        PropIn(css, ".tm-chat__thread-badge", "min-width", CoarsePointer).Should().BeNull();
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ActionBar_SitsInTheFlowOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__actions", "position", CoarsePointer).Should().Be(
            "static", "an always-revealed floating bar would cover the bubble it belongs to — " +
            "on touch it becomes a wrapped row under the message");
        PropIn(css, ".tm-chat__actions", "align-self", CoarsePointer).Should().Be("flex-start");
        PropIn(css, ".tm-chat__actions", "flex-wrap", CoarsePointer).Should().Be("wrap");
        PropIn(css, ".tm-chat__actions", "max-width", CoarsePointer).Should().Be("100%");
        PropIn(css, ".tm-chat__actions", "box-shadow", CoarsePointer).Should().Be(
            "none", "a flow row needs no floating-card shadow");
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ActionBar_PositionOffsetsAreResetOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        foreach (var alignment in new[] { "incoming", "outgoing" })
        {
            var selector = $".tm-chat__message--{alignment} .tm-chat__actions";
            PropIn(css, selector, "left", CoarsePointer).Should().Be(
                "auto", $"{alignment}: the horizontal offset is meaningless once the bar is static");
            PropIn(css, selector, "right", CoarsePointer).Should().Be("auto");
        }
    }

    [Theory]
    [InlineData("scoped")]
    [InlineData("fallback")]
    public void ComposerInput_StepsUpTheTypeScaleOnCoarsePointers(string variant)
    {
        var css = variant == "scoped" ? ChatRazorCss() : FallbackCss();

        PropIn(css, ".tm-chat__input", "font-size", CoarsePointer).Should().Be(
            "var(--tm-font-size-md)",
            "0.875rem body text is small under a finger; the token keeps the step host-tunable");
        PropIn(css, ".tm-chat__input", "font-size").Should().Be(
            "var(--tm-font-size-sm)", "the desktop composer keeps today's type size");
    }

    // ── 4. The FillParent parameter renders the modifier class ───────────────

    private static readonly Tempo.Blazor.Models.ChatUser Me = new("u1", "Alice");

    [Fact]
    public void FillParent_RendersTheTmChatFillModifierClass()
    {
        var cut = Render<Tempo.Blazor.Components.Chat.TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.Messages, [])
            .Add(c => c.FillParent, true));

        cut.Find(".tm-chat").ClassList.Should().Contain("tm-chat--fill",
            "FillParent opts the chat into being a flex child of the host layout");
    }

    [Fact]
    public void FillParent_DefaultFalse_RendersNoModifierClass()
    {
        var cut = Render<Tempo.Blazor.Components.Chat.TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.Messages, []));

        cut.Find(".tm-chat").ClassList.Should().NotContain("tm-chat--fill",
            "the default keeps today's height: 100% + min-height knob behavior");
    }
}
