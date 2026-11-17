using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Mobile presentation of <see cref="TmModal"/>. The presentation is a parameter so a test can force
/// it without a DOM, and Auto follows the viewport scope rather than the container the trigger sits
/// in. These tests pin the markup contract the stylesheet keys off.
/// </summary>
public class TmModalPresentationTests : LocalizationTestBase
{
    [Theory]
    [InlineData(MobilePresentation.Dialog, "tm-modal--dialog")]
    [InlineData(MobilePresentation.Sheet, "tm-modal--sheet")]
    [InlineData(MobilePresentation.Fullscreen, "tm-modal--fullscreen-presentation")]
    public void ForcedPresentation_RendersThatVariant(MobilePresentation presentation, string expectedClass)
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, presentation)
            .AddChildContent("<p>Body</p>"));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void Auto_WithNoViewportScope_RendersTheInitialMode_WhichIsADialog()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("desktop");
        overlay.ClassList.Should().Contain("tm-modal--dialog");
        overlay.ClassList.Should().NotContain("tm-modal--sheet");
        cut.FindAll(".tm-viewport-probe").Should().ContainSingle(
            "with no viewport scope an Auto overlay measures the viewport itself, so the first frame is not stuck on InitialMode");
    }

    [Fact]
    public void Auto_FollowsTheViewportScope_EvenWhenTheContainerIsDesktop()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("mobile");
        overlay.ClassList.Should().Contain("tm-modal--sheet",
            "Auto presents a sheet on a mobile viewport, regardless of the container the trigger sits in");
    }

    [Fact]
    public void Auto_PresentsADialogOnTabletAndDesktop()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Tablet));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("tablet");
        overlay.ClassList.Should().Contain("tm-modal--dialog");
    }

    [Fact]
    public void AutoFullscreen_IsTheTallSurfaceVariantOfAuto()
    {
        // A tall form must not be clipped by a sheet: AutoFullscreen takes the whole viewport on a
        // mobile layout and stays the centred dialog everywhere else.
        var mobile = Render<ViewportHost>(p => p
            .Add(x => x.ViewportMode, TmLayoutMode.Mobile)
            .Add(x => x.Presentation, MobilePresentation.AutoFullscreen));
        mobile.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--fullscreen-presentation");
        mobile.Find(".tm-modal-overlay").ClassList.Should().NotContain("tm-modal--sheet");

        var tablet = Render<ViewportHost>(p => p
            .Add(x => x.ViewportMode, TmLayoutMode.Tablet)
            .Add(x => x.Presentation, MobilePresentation.AutoFullscreen));
        tablet.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--dialog");
        tablet.Find(".tm-modal-overlay").ClassList.Should().NotContain("tm-modal--fullscreen-presentation");
    }

    [Fact]
    public void ForcedLayoutMode_RendersTheMatchingPresentationWithoutADom()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<p>Body</p>"));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public void Sheet_RendersADragHandle_AndKeepsTheFooterASiblingOfTheBody()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, MobilePresentation.Sheet)
            .Add(m => m.Footer, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Save</button>")))
            .AddChildContent("<p>Body</p>"));

        var handle = cut.Find(".tm-sheet__handle");
        handle.GetAttribute("aria-hidden").Should().Be("true",
            "a modal sheet handle is decorative; the drawer handle is the one that takes keyboard focus");
        handle.GetAttribute("tabindex").Should().BeNull();
        var modal = cut.Find(".tm-modal");
        modal.InnerHtml.Should().Contain("tm-modal-body");
        modal.InnerHtml.Should().Contain("tm-modal-footer");
    }

    [Fact]
    public void Sheet_WithoutADismissGesture_RendersNoDeadGrabber()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, MobilePresentation.Sheet)
            .Add(m => m.CloseOnOverlayClick, false)
            .AddChildContent("<p>Body</p>"));

        cut.FindAll(".tm-sheet__handle").Should().BeEmpty(
            "the grabber never dismisses when the overlay click is off, so painting it invites a dead swipe");
    }

    [Fact]
    public void SheetFooter_LabelsTruncateInsteadOfOverflowingAt320()
    {
        var modalCss = File.ReadAllText(Path.Combine(FindRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_modal.css"));
        var buttonCss = File.ReadAllText(Path.Combine(FindRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_button.css"));

        var inline = RuleFor(modalCss, ".tm-modal-overlay.tm-modal--sheet .tm-modal-footer--inline > .tm-btn");
        inline.Should().Contain("min-width: auto",
            "min-width: fit-content pushed a long FR label out of a 320px sheet footer and min-width: 0 cut ordinary labels with a forced 50/50 split; auto wraps them to their own row first");
        inline.Should().Contain("max-width: 100%",
            "a single label wider than the whole row is capped, and only that one truncates");
        inline.Should().NotContain("height",
            "a height claim on .tm-btn from another stylesheet would outrank the tm-btn-sm/md size modifiers");

        var label = RuleFor(modalCss, ".tm-modal-overlay.tm-modal--sheet .tm-modal-footer .tm-btn-label");
        label.Should().Contain("text-overflow: ellipsis",
            "the truncated label keeps the button's size contract and shows the full text in its title");
        label.Should().Contain("display: block").And.Contain("min-width: 0",
            "the span ships as display: contents; the sheet footer is the opt-in that blockifies and shrinks it");

        RuleFor(buttonCss, ".tm-btn-label").Should().Contain("display: contents",
            "the span must be geometry-neutral by default — an empty flex span grew icon-only buttons by a gap");
    }

    [Fact]
    public void SheetFooterButtons_CarryTheirLabelAsTheTitle_DesktopOnesDoNot()
    {
        var sheet = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, MobilePresentation.Sheet)
            .Add(m => m.ShowDefaultFooterButtons, true)
            .AddChildContent("<p>Body</p>"));

        sheet.Find(".tm-modal-btn-ok").GetAttribute("title").Should().NotBeNullOrWhiteSpace(
            "the sheet footer may truncate the label, so the full text rides in the title");
        sheet.Find(".tm-modal-btn-cancel").GetAttribute("title").Should().NotBeNullOrWhiteSpace();

        var dialog = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, MobilePresentation.Dialog)
            .Add(m => m.ShowDefaultFooterButtons, true)
            .AddChildContent("<p>Body</p>"));

        dialog.Find(".tm-modal-btn-ok").GetAttribute("title").Should().BeNull(
            "a fully visible desktop label must not grow an 'OK' tooltip");
        dialog.Find(".tm-modal-btn-cancel").GetAttribute("title").Should().BeNull();
        dialog.Find(".tm-modal-btn-ok .tm-btn-label").Should().NotBeNull("the label is a span the footer can truncate");
    }

    /// <summary>Reads the declaration block of the last rule that declares the selector verbatim.</summary>
    private static string RuleFor(string css, string selector)
    {
        var start = css.LastIndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the stylesheet must declare {selector}");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        return css[open..close];
    }

    /// <summary>
    /// Finds the at-rule block whose balanced-brace body satisfies <paramref name="predicate"/>.
    /// The body runs to the at-rule's PAIRED closing brace, so nested media bodies never leak a
    /// distant match into the wrong block.
    /// </summary>
    private static (int start, string body)? MediaBlockOf(string css, string atRule, Func<string, bool> predicate)
    {
        var search = 0;
        while (true)
        {
            var start = css.IndexOf(atRule, search, StringComparison.Ordinal);
            if (start < 0) return null;
            var open = css.IndexOf('{', start);
            open.Should().BeGreaterThan(-1, $"the {atRule} block must open a body");
            var depth = 0;
            for (var i = open; i < css.Length; i++)
            {
                if (css[i] == '{') depth++;
                else if (css[i] == '}' && --depth == 0)
                {
                    var body = css[(open + 1)..i];
                    if (predicate(body)) return (start, body);
                    break;
                }
            }

            search = start + atRule.Length;
        }
    }

    [Fact]
    public void StackedInOrderFooter_PinTheOrderTheScrollCapAndTheLabelWrap()
    {
        // Plan 98c80bc3 (Fáze 6): FooterLayout.StackedInOrder paints the footer children in DOM
        // order (the legacy Stacked is column-reverse) and a stacked footer with more actions
        // than fit scrolls under a max-height cap instead of pushing the dialog taller, with
        // button labels that wrap instead of truncating.
        var css = File.ReadAllText(Path.Combine(FindRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_modal.css"));

        var order = RuleFor(css, ".tm-modal-footer--stacked-in-order,");
        order.Should().Contain("flex-direction: column",
            "StackedInOrder is the DOM-order column; Stacked stays column-reverse");

        var stacked = RuleFor(css, ".tm-modal-footer--stacked,");
        stacked.Should().Contain("max-height: var(--tm-modal-footer-max-height)",
            "a stacked footer taller than the cap scrolls instead of growing the dialog");
        // The cap must stay an inherited :root token (tokens.css): a declaration on the footer
        // itself would shadow the inherited value, so a host capping the footer from the modal /
        // panel root (fze-6) would silently stop working.
        stacked.Should().NotContain("--tm-modal-footer-max-height:",
            "the footer must READ the token, not declare it — an on-element declaration would shadow a host cap inherited from the modal/panel root");
        var tokens = File.ReadAllText(Path.Combine(FindRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));
        tokens.Should().Contain("--tm-modal-footer-max-height: 40dvh",
            "the cap token ships on :root so the footer's var() resolves at every width and hosts can override it");
        ThemeCss.BundledCss().Should().Contain("max-height:var(--tm-modal-footer-max-height)",
            "the committed tempo-blazor.bundled.css must ship the same rule (consumers link the bundle)");
        stacked.Should().Contain("overflow-y: auto");
        stacked.Should().Contain("flex-shrink: 0",
            "a scroller loses its content-sized minimum in the flex column — without it the footer compresses instead of scrolling");

        // The label-wrap rule, found through its declaration (a later pointer:coarse block reuses
        // the same selector for the touch floor — see StackedFooterButtons_KeepTheCoarseTouchFloor).
        var wrap = css.IndexOf("white-space: normal", StringComparison.Ordinal);
        wrap.Should().BeGreaterThan(-1, "the stylesheet must declare the stacked label wrap");
        var open = css.LastIndexOf('{', wrap);
        var close = css.IndexOf('}', open);
        var stackedBtn = css[open..close];
        stackedBtn.Should().Contain("white-space: normal").And.Contain("overflow-wrap: anywhere")
            .And.Contain("height: auto").And.Contain("min-height: var(--tm-input-height-md)",
                "a long button label wraps onto its own row instead of truncating — and the fixed .tm-btn height must not clip the second line");
    }

    [Fact]
    public void StackedFooterButtons_KeepTheCoarseTouchFloor()
    {
        // The label-wrap rule's (0,3,0) min-height silently beat the shared (pointer:coarse)
        // footer floor (.tm-dialog-footer .tm-btn → --tm-touch-target): a phone's stacked actions
        // dropped to 38px, pinned red by
        // DeleteDialog_AtEveryViewport_TrashIcon_DeleteAboveCancelOnMobileSheet (plan 98c80bc3).
        var css = File.ReadAllText(Path.Combine(FindRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_modal.css"));

        // The label-wrap rule the floor has to follow in the cascade (equal specificity → later wins).
        var wrap = css.IndexOf("white-space: normal", StringComparison.Ordinal);
        wrap.Should().BeGreaterThan(-1, "the stylesheet must declare the stacked label wrap");

        // The stylesheet has several (pointer:coarse) blocks — only the one whose BALANCED body
        // carries the stacked selectors restores the floor; a distant match in another block would
        // be dead CSS for the stacked footer.
        var floor = MediaBlockOf(css, "@media (pointer: coarse)",
            body => body.Contains(".tm-dialog-footer.tm-modal-footer--stacked .tm-btn", StringComparison.Ordinal));
        floor.Should().NotBeNull(
            "the stacked footer needs its own (pointer:coarse) block carrying the stacked selectors");
        floor!.Value.body.Should().Contain(".tm-modal-footer--stacked .tm-btn",
            "the floor rides the same stacked selectors — only an equal-or-higher specificity restores it");
        floor.Value.body.Should().Contain("min-height: var(--tm-touch-target)",
            "a finger needs the shared 44px target on the stacked footer's actions");
        floor.Value.start.Should().BeGreaterThan(wrap,
            "the floor must be declared AFTER the label-wrap rule — same specificity, so the later min-height wins the cascade");
    }

    [Fact]
    public void StackedFooter_IsNotInsideAMediaQuery()
    {
        var css = File.ReadAllText(Path.Combine(FindRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_modal.css"));
        var rule = css.IndexOf(".tm-modal-footer--stacked", StringComparison.Ordinal);
        rule.Should().BeGreaterThan(0);
        var media = css.LastIndexOf("@media", rule, StringComparison.Ordinal);
        if (media >= 0)
        {
            css.IndexOf('}', media).Should().BeLessThan(rule, "the stacked footer applies at every width");
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Theory]
    [InlineData(FooterLayout.Inline, "tm-modal-footer--inline")]
    [InlineData(FooterLayout.Stacked, "tm-modal-footer--stacked")]
    [InlineData(FooterLayout.StackedInOrder, "tm-modal-footer--stacked-in-order")]
    public void FooterLayout_IsAClass_SoTheStylesheetCanKeepButtonsSideBySide(FooterLayout layout, string expectedClass)
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.FooterLayout, layout)
            .Add(m => m.ShowDefaultFooterButtons, true)
            .AddChildContent("<p>Body</p>"));

        cut.Find(".tm-modal-footer").ClassList.Should().Contain(expectedClass);
        cut.Find(".tm-modal").GetAttribute("data-restore-target").Should().BeNull();
    }

    [Fact]
    public void RestoreFocusTarget_IsForwardedToTheScope()
    {
        var cut = Render<RestoreHost>();

        cut.Find(".tm-modal").GetAttribute("data-restore-target").Should().Be("trigger");
    }

    [Fact]
    public void PositionBottom_AnchorsThePanel_WithNoGap()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.Position, ModalPosition.Bottom)
            .AddChildContent("<p>Body</p>"));

        var container = cut.Find(".tm-modal-container");
        container.ClassList.Should().Contain("tm-modal--bottom-anchored");
        container.ClassList.Should().Contain("tm-modal--bottom-flush",
            "ModalPosition.Bottom sits flush with the viewport edge at every width, with no overlay padding and no modal margin");
    }

    [Fact]
    public void ResolvedLayoutChanged_FiresOnATransition_NotForTheInitialValue()
    {
        var seen = new List<TmLayoutMode>();
        var cut = Render<ViewportHost>(p => p
            .Add(x => x.ViewportMode, TmLayoutMode.Desktop)
            .Add(x => x.OnResolved, EventCallback.Factory.Create<TmLayoutMode>(this, seen.Add)));

        seen.Should().BeEmpty("the initial resolved value is not a change");

        cut.Render(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        seen.Should().Equal(TmLayoutMode.Mobile);
    }

    private sealed class RestoreHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "id", "trigger");
            builder.AddContent(2, "Open");
            builder.CloseElement();

            builder.OpenComponent<TmModal>(3);
            builder.AddAttribute(4, "Show", true);
            builder.AddAttribute(5, "Title", "Create");
            builder.AddAttribute(6, "RestoreFocusTargetId", "trigger");
            builder.AddAttribute(7, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<p>Body</p>")));
            builder.CloseComponent();
        }
    }

    /// <summary>Cascades a desktop container context beside a named viewport context.</summary>
    private sealed class ViewportHost : ComponentBase
    {
        [Parameter] public TmLayoutMode ViewportMode { get; set; } = TmLayoutMode.Mobile;

        [Parameter] public EventCallback<TmLayoutMode> OnResolved { get; set; }

        /// <summary>The modal's mobile presentation; AutoFullscreen covers the tall-surface variant.</summary>
        [Parameter] public MobilePresentation Presentation { get; set; } = MobilePresentation.Auto;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Desktop, TmLayoutMode.Desktop));
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<CascadingValue<TmLayoutContext>>(0);
                inner.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Auto, ViewportMode));
                inner.AddAttribute(2, "Name", TmLayoutScopes.Viewport);
                inner.AddAttribute(3, "ChildContent", (RenderFragment)(modal =>
                {
                    modal.OpenComponent<TmModal>(0);
                    modal.AddAttribute(1, "Show", true);
                    modal.AddAttribute(2, "Title", "Create");
                    modal.AddAttribute(3, "ResolvedLayoutChanged", OnResolved);
                    modal.AddAttribute(4, "MobilePresentation", Presentation);
                    modal.AddAttribute(5, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<p>Body</p>")));
                    modal.CloseComponent();
                }));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
