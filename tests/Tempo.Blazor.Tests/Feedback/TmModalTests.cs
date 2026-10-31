using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Feedback;

public class TmModalTests : LocalizationTestBase
{
    public TmModalTests()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        Services.AddSingleton(jsRuntime);
    }

    [Fact]
    public void Footer_IsAbsent_WhenThereIsNothingToShow()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Modal")
            .AddChildContent("<p>Modal content</p>"));

        cut.FindAll(".tm-modal-footer").Should().BeEmpty(
            "an empty footer bar is a stray border; it renders only with a footer fragment or the default buttons");
    }

    [Fact]
    public void Footer_Renders_WhenTheDefaultButtonsAreOn()
    {
        UseCzechLocalization();

        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Modal")
            .Add(m => m.ShowDefaultFooterButtons, true)
            .AddChildContent("<p>Modal content</p>"));

        cut.Find(".tm-modal-btn-cancel").TextContent.Trim().Should().Be("Zrušit");
        cut.Find(".tm-modal-btn-ok").TextContent.Trim().Should().Be("OK");
    }

    [Fact]
    public void FooterButtons_UseTheCallerText_WhenOneIsPassed()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Modal")
            .Add(m => m.ShowDefaultFooterButtons, true)
            .Add(m => m.OkButtonText, "Uložit")
            .Add(m => m.CancelButtonText, "Zpět")
            .AddChildContent("<p>Modal content</p>"));

        cut.Find(".tm-modal-btn-ok").TextContent.Trim().Should().Be("Uložit");
        cut.Find(".tm-modal-btn-cancel").TextContent.Trim().Should().Be("Zpět");
    }

    #region Rendering Tests

    [Fact]
    public void Modal_IsVisible_WhenShowTrue()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Modal")
            .AddChildContent("<p>Modal content</p>"));

        // Assert
        cut.Find(".tm-modal-overlay").Should().NotBeNull();
        cut.Find(".tm-modal").Should().NotBeNull();
    }

    [Fact]
    public void Modal_HasDialogAccessibilityAttributes()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Accessible modal"));

        var modal = cut.Find(".tm-modal");
        modal.GetAttribute("role").Should().Be("dialog");
        modal.GetAttribute("aria-modal").Should().Be("true");
        modal.GetAttribute("tabindex").Should().Be("-1");

        var labelledBy = modal.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find(".tm-modal-title").GetAttribute("id").Should().Be(labelledBy);
    }

    [Fact]
    public void Modal_OpenThenClose_FocusTrapLifecycle_DoesNotThrow()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Trap"));

        var act = () => cut.Render(p => p.Add(m => m.Show, false));
        act.Should().NotThrow();
    }

    [Fact]
    public void Modal_IsHidden_WhenShowFalse()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Title, "Test Modal"));

        // Assert
        cut.FindAll(".tm-modal-overlay").Count.Should().Be(0);
    }

    [Fact]
    public void Modal_DisplaysTitle()
    {
        // Arrange
        const string title = "My Modal Title";

        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, title));

        // Assert
        cut.Find(".tm-modal-title").TextContent.Should().Be(title);
    }

    [Fact]
    public void Modal_DisplaysChildContent()
    {
        // Arrange
        const string content = "This is the modal body content";

        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .AddChildContent(content));

        // Assert
        cut.Find(".tm-modal-body").TextContent.Should().Contain(content);
    }

    #endregion

    #region Interaction Tests

    [Fact]
    public void Modal_ClickOverlay_CallsOnClose()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true))
            .Add(m => m.CloseOnOverlayClick, true));

        // Act
        cut.Find(".tm-modal-overlay").Click();

        // Assert
        closeCalled.Should().BeTrue();
    }

    [Fact]
    public void Modal_ClickOverlay_DoesNotClose_WhenCloseOnOverlayClickFalse()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true))
            .Add(m => m.CloseOnOverlayClick, false));

        // Act
        cut.Find(".tm-modal-overlay").Click();

        // Assert
        closeCalled.Should().BeFalse();
    }

    [Fact]
    public void Modal_ClickCloseButton_CallsOnClose()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowCloseButton, true)
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        cut.Find(".tm-modal-close").Click();

        // Assert
        closeCalled.Should().BeTrue();
    }

    [Fact]
    public void Modal_PressEscape_CallsOnClose()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.CloseOnEscape, true)
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        // Escape is delivered by the focus-trap module at the document level; the module calls back
        // into the scope's JSInvokable, which the modal wires to OnClose.
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        // Assert
        closeCalled.Should().BeTrue();
    }

    [Fact]
    public void Modal_RestoreFocusFalse_OptsTheScopeOutOfTheRestore()
    {
        // The default keeps the restore (no data-restore-focus attribute on the scope root);
        // RestoreFocus=false renders the opt-out flag the tm-focus-trap module reads and skips.
        var defaultCut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));
        defaultCut.Find("div.tm-focus-scope").HasAttribute("data-restore-focus").Should().BeFalse(
            "the default restores focus, so the opt-out flag is absent");

        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.RestoreFocus, false));

        cut.Find("div.tm-focus-scope").GetAttribute("data-restore-focus").Should().Be("false",
            "RestoreFocus=false renders data-restore-focus=\"false\" so the module skips the restore");
    }

    #endregion

    #region Size Tests

    [Theory]
    [InlineData(ModalSize.Small, "tm-modal--sm")]
    [InlineData(ModalSize.Medium, "tm-modal--md")]
    [InlineData(ModalSize.Large, "tm-modal--lg")]
    [InlineData(ModalSize.XLarge, "tm-modal--xl")]
    [InlineData(ModalSize.Fullscreen, "tm-modal--fullscreen")]
    public void Modal_AppliesSizeClass(ModalSize size, string expectedClass)
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Size, size));

        // Assert
        cut.Find(".tm-modal").ClassList.Should().Contain(expectedClass);
    }

    #endregion

    #region Animation Tests

    [Fact]
    public void Modal_HasAnimationClasses()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Animated, true));

        // Assert
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--animated");
    }

    [Fact]
    public void Modal_Show_AddsVisibleClass()
    {
        // Arrange
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Title, "Test"));

        // Act
        cut.Render(p => p.Add(m => m.Show, true));

        // Assert
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--visible");
    }

    #endregion

    #region Footer Tests

    [Fact]
    public void Modal_ShowFooter_RendersFooter()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowFooter, true)
            .Add(m => m.Footer, builder => builder.AddContent(0, "Footer content")));

        // Assert
        cut.Find(".tm-modal-footer").Should().NotBeNull();
        cut.Find(".tm-modal-footer").TextContent.Should().Be("Footer content");
    }

    #endregion

    #region Accessibility Tests

    [Fact]
    public void Modal_HasRoleDialog()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        // Assert
        cut.Find(".tm-modal").GetAttribute("role").Should().Be("dialog");
    }

    [Fact]
    public void Modal_HasAriaModal()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        // Assert
        cut.Find(".tm-modal").GetAttribute("aria-modal").Should().Be("true");
    }

    [Fact]
    public void Modal_HasAriaLabelledBy()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Title"));

        // Assert
        var modal = cut.Find(".tm-modal");
        var ariaLabelledBy = modal.GetAttribute("aria-labelledby");
        ariaLabelledBy.Should().NotBeNull();
        
        // Verify the title element has the matching id
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(ariaLabelledBy);
    }

    #endregion

    #region Close Button Tests

    [Fact]
    public void Modal_CloseButtonAdditionalAttributes_LandOnTheCloseButton()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.CloseButtonAdditionalAttributes, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = "host-close",
                ["data-foo"] = "bar"
            }));

        // Assert
        var close = cut.Find(".tm-modal-close");
        close.GetAttribute("data-testid").Should().Be("host-close");
        close.GetAttribute("data-foo").Should().Be("bar");
    }

    [Fact]
    public void Modal_CloseButtonAdditionalAttributes_DoNotReplaceBuiltins()
    {
        // Act — a host splat may only ADD attributes: type, class, @onclick and aria-label win.
        bool closeCalled = false;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true))
            .Add(m => m.CloseButtonAdditionalAttributes, new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "submit",
                ["class"] = "host-class",
                ["aria-label"] = "Host label"
            }));

        // Assert
        var close = cut.Find(".tm-modal-close");
        close.GetAttribute("type").Should().Be("button", "the built-in type wins over the splat");
        close.ClassList.Should().Contain("tm-modal-close", "the built-in class wins over the splat");
        close.ClassList.Should().NotContain("host-class");
        close.GetAttribute("aria-label").Should().NotBe("Host label", "the built-in accessible name wins over the splat");

        close.Click();
        closeCalled.Should().BeTrue("the built-in click handler survives the splat");
    }

    [Fact]
    public void Modal_ShowCloseButtonFalse_RendersNoCloseButton()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowCloseButton, false));

        // Assert
        cut.FindAll(".tm-modal-close").Should().BeEmpty();
    }

    [Fact]
    public void Modal_Subtitle_IsTheScopeAriaDescribedByTarget()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Subtitle, "Described"));

        // Assert
        var modal = cut.Find(".tm-modal");
        var describedBy = modal.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNull("a subtitle describes the dialog for screen readers");
        cut.Find(".tm-modal-subtitle").GetAttribute("id").Should().Be(describedBy);
    }

    [Fact]
    public void Modal_WithoutSubtitle_RendersNoAriaDescribedBy()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        // Assert
        cut.Find(".tm-modal").GetAttribute("aria-describedby").Should().BeNull();
    }

    #endregion

    #region Position Tests

    [Theory]
    [InlineData(ModalPosition.Center, "tm-modal--center")]
    [InlineData(ModalPosition.Top, "tm-modal--top")]
    [InlineData(ModalPosition.Bottom, "tm-modal--bottom")]
    public void Modal_AppliesPositionClass(ModalPosition position, string expectedClass)
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Position, position));

        // Assert
        cut.Find(".tm-modal-container").ClassList.Should().Contain(expectedClass);
    }

    #endregion

    #region InitialFocusSelector (phase-4 review, additive)

    [Fact]
    public void InitialFocusSelector_ReachesTheFocusScope()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.InitialFocusSelector, "input[name='pick']:checked"));

        var scope = cut.Find(".tm-focus-scope");
        scope.GetAttribute("data-initial-focus-selector").Should().Be("input[name='pick']:checked",
            "the selector is an additive alternative to the id for targets without a stable id");
    }

    [Fact]
    public void InitialFocusSelector_IsNullByDefault()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        cut.Find(".tm-focus-scope").HasAttribute("data-initial-focus-selector").Should().BeFalse(
            "the default markup is unchanged");
    }

    #endregion

    #region Header polish (phase 7c, remaining task 9a1e5f44)

    /// <summary>
    /// The TmModal header's × was optically 1-2 px low in EVERY dialog of the library (the phase-3 UX
    /// finding): the 2rem close box shares its top edge with the title's line box, whose line-height is
    /// only 1.3 x font-size-lg, so the icon's centre sat ~4 px below the title's centre. The frame is
    /// shared, so the fix lives ONCE in the core stylesheet and the whole library inherits it.
    /// </summary>
    [Fact]
    public void Modal_Header_CloseButton_SharesTitleLineBox()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Modal")
            .Add(m => m.Subtitle, "With a subtitle")
            .Add(m => m.Icon, "info")
            .AddChildContent("<p>content</p>"));

        var header = cut.Find(".tm-modal-header");
        var close = cut.Find(".tm-modal-close");
        close.ParentElement.Should().NotBeNull();
        close.ParentElement!.ClassList.Should().Contain("tm-modal-header",
            "the close button is a ROW CHILD of the header, never an absolute overlay");
        header.Should().NotBeNull();
        header.QuerySelector(".tm-modal-title").Should().NotBeNull();

        var css = ThemeCss.ComponentCss("_modal.css");
        css.Should().MatchRegex(
                @"\.tm-modal-close\s*\{[^}]*margin-block-start:\s*calc\(\(var\(--tm-font-size-lg\)\s*\*\s*1\.3\s*-\s*var\(--tm-modal-close-size\)\)\s*/\s*2\)",
            "the close box is offset so its CENTRE meets the first title line box's centre");
        css.Should().MatchRegex(@"\.tm-modal-close\s*\{[^}]*--tm-modal-close-size:\s*2rem[^}]*width:\s*var\(--tm-modal-close-size\)[^}]*height:\s*var\(--tm-modal-close-size\)",
            "the nominal (fine pointer) box keeps its 2rem size and reads the SAME variable the offset reads");

        // A coarse pointer raises the box to the 44px touch floor; the offset must follow it, or the
        // icon lands ~6px low again on a phone (where every dialog is a sheet/fullscreen panel). The
        // override has to live in the SAME stylesheet AFTER the base rule: breakpoints.css is imported
        // FIRST, so a knob declared there loses to this file's own default at equal specificity.
        css.Should().MatchRegex(
                @"@media\s*\(pointer:\s*coarse\)\s*\{\s*\.tm-modal-close\s*\{\s*--tm-modal-close-size:\s*var\(--tm-touch-target\)",
            "the touch-floor size is published through the same variable the optical offset reads");
        css.IndexOf("--tm-modal-close-size: var(--tm-touch-target)", StringComparison.Ordinal)
            .Should().BeGreaterThan(css.IndexOf("--tm-modal-close-size: 2rem", StringComparison.Ordinal),
                "the media override must come after the base declaration, or it is dead CSS");

        ThemeCss.BundledCss().Should().Contain("calc((var(--tm-font-size-lg) * 1.3 - var(--tm-modal-close-size)) / 2)",
            "the committed tempo-blazor.bundled.css must ship the same rule (consumers link the bundle)");
    }

    #endregion

    #region HeaderActions Tests (Fáze 10)

    [Fact]
    public void HeaderActions_render_inside_header_before_close_button()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowCloseButton, true)
            .Add(m => m.HeaderActions, "<button type=\"button\" class=\"tm-modal-header-text-close\">Close</button>"));

        // Assert: the actions wrapper sits in the header row, AFTER the header content and
        // BEFORE the built-in close button.
        var header = cut.Find(".tm-modal-header");
        header.QuerySelector(".tm-modal-header-actions").Should().NotBeNull();
        var contentIndex = header.InnerHtml.IndexOf("tm-modal-header-content", StringComparison.Ordinal);
        var actionsIndex = header.InnerHtml.IndexOf("tm-modal-header-actions", StringComparison.Ordinal);
        var closeIndex = header.InnerHtml.IndexOf("tm-modal-close", StringComparison.Ordinal);
        (actionsIndex > contentIndex && actionsIndex < closeIndex).Should().BeTrue(
            "the actions slot renders between the header content and the close button");
        cut.Find(".tm-modal-header-actions .tm-modal-header-text-close").TextContent.Should().Be("Close");
    }

    [Fact]
    public void HeaderActions_null_keeps_header_dom_unchanged()
    {
        // Act — default (null) HeaderActions
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        // Assert: no actions wrapper, the plain close button stays.
        cut.FindAll(".tm-modal-header-actions").Should().BeEmpty();
        cut.Find(".tm-modal-header .tm-modal-close").Should().NotBeNull();
    }

    [Fact]
    public void HeaderActions_are_inside_focus_scope()
    {
        // Act
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.HeaderActions, "<button type=\"button\" id=\"header-action-btn\">Close</button>"));

        // Assert: the slot renders inside the TmFocusScope subtree, so the Tab cycle covers it.
        var scope = cut.Find(".tm-focus-scope");
        scope.QuerySelector(".tm-modal-header-actions #header-action-btn").Should().NotBeNull(
            "host header actions are focusables inside the trap");
    }

    [Fact]
    public void HeaderActions_button_precedes_body_focusables_in_document_order()
    {
        // Act — a header action AND a body focusable rendered together.
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowCloseButton, true)
            .Add(m => m.HeaderActions, "<button type=\"button\" id=\"header-action-btn\">Close</button>")
            .AddChildContent("<button type=\"button\" id=\"body-action-btn\">Body</button>"));

        // Assert: in DOCUMENT order the header action precedes every body focusable — the
        // focus trap's cycle reaches the header row before the body content (querySelectorAll
        // returns its matches in document order).
        var ordered = cut.Find(".tm-modal-container")
            .QuerySelectorAll("#header-action-btn, #body-action-btn");
        ordered.Should().HaveCount(2);
        ordered[0].Id.Should().Be("header-action-btn",
            "the header action is the first focusable in document order");
    }

    #endregion
}
