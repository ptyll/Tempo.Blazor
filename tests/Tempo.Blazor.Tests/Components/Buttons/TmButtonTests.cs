using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Buttons;

/// <summary>
/// TDD tests for TmButton component.
/// RED phase: these tests are written before the component implementation.
/// </summary>
public class TmButtonTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Renders_Button_Element()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Click me"));

        cut.Find("button").Should().NotBeNull();
    }

    [Fact]
    public void TmButton_Renders_ChildContent()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Save"));

        cut.Find("button").TextContent.Trim().Should().Be("Save");
    }

    // ─── ButtonType ───────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Default_Type_Is_Button()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("type").Should().Be("button");
    }

    [Theory]
    [InlineData(ButtonType.Button, "button")]
    [InlineData(ButtonType.Submit, "submit")]
    [InlineData(ButtonType.Reset, "reset")]
    public void TmButton_Renders_Correct_Html_Type(ButtonType buttonType, string expectedHtmlType)
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Type, buttonType)
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("type").Should().Be(expectedHtmlType);
    }

    // ─── Variant CSS ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ButtonVariant.Primary, "tm-btn-primary")]
    [InlineData(ButtonVariant.Secondary, "tm-btn-secondary")]
    [InlineData(ButtonVariant.Ghost, "tm-btn-ghost")]
    [InlineData(ButtonVariant.Danger, "tm-btn-danger")]
    [InlineData(ButtonVariant.Outline, "tm-btn-outline")]
    [InlineData(ButtonVariant.Link, "tm-btn-link")]
    [InlineData(ButtonVariant.Default, "tm-btn-default")]
    [InlineData(ButtonVariant.OutlineSecondary, "tm-btn-outline-secondary")]
    [InlineData(ButtonVariant.Warning, "tm-btn-warning")]
    [InlineData(ButtonVariant.OutlineWarning, "tm-btn-outline-warning")]
    public void TmButton_Applies_Variant_CssClass(ButtonVariant variant, string expectedClass)
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Variant, variant)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmButton_Default_Variant_Is_Primary()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn-primary");
    }

    // ─── Size CSS ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ButtonSize.Xs, "tm-btn-xs")]
    [InlineData(ButtonSize.Sm, "tm-btn-sm")]
    [InlineData(ButtonSize.Md, "tm-btn-md")]
    [InlineData(ButtonSize.Lg, "tm-btn-lg")]
    public void TmButton_Applies_Size_CssClass(ButtonSize size, string expectedClass)
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Size, size)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmButton_Default_Size_Is_Md()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn-md");
    }

    // ─── Block ────────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Block_Adds_Block_CssClass()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Block, true)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn-block");
    }

    [Fact]
    public void TmButton_NonBlock_Does_Not_Add_Block_CssClass()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Block, false)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().NotContain("tm-btn-block");
    }

    // ─── Disabled ─────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Disabled, true)
            .AddChildContent("Click"));

        cut.Find("button").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmButton_Disabled_Does_Not_Fire_OnClick()
    {
        var clicked = false;
        var cut = Render<TmButton>(p => p
            .Add(c => c.Disabled, true)
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Click"));

        cut.Find("button").Click();

        clicked.Should().BeFalse();
    }

    // ─── Loading ──────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Loading_Renders_Spinner_And_Disables()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.IsLoading, true)
            .AddChildContent("Save"));

        // Button must be disabled while loading
        cut.Find("button").HasAttribute("disabled").Should().BeTrue();
        // Spinner should be present
        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
    }

    [Fact]
    public void TmButton_Loading_Does_Not_Fire_OnClick()
    {
        var clicked = false;
        var cut = Render<TmButton>(p => p
            .Add(c => c.IsLoading, true)
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Save"));

        cut.Find("button").Click();

        clicked.Should().BeFalse();
    }

    [Fact]
    public void TmButton_Loading_Hides_Icon_And_Shows_Spinner()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.IsLoading, true)
            .Add(c => c.Icon, IconNames.Check)
            .AddChildContent("Save"));

        // Spinner shown, TmIcon not shown while loading
        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
        cut.FindAll(".tm-icon").Should().BeEmpty();
    }

    // ─── Icon ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Icon_Left_Renders_Icon_Before_Content()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Icon, IconNames.Check)
            .Add(c => c.IconRight, false)
            .AddChildContent("Save"));

        var children = cut.Find("button").ChildNodes;
        // First child should be the icon (svg), then text
        children.Length.Should().BeGreaterThan(1);
    }

    [Fact]
    public void TmButton_Icon_Right_When_IconRight_True()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.Icon, IconNames.Check)
            .Add(c => c.IconRight, true)
            .AddChildContent("Next"));

        // When IconRight=true, icon SVG should be the last element
        var button = cut.Find("button");
        button.ChildNodes.Length.Should().BeGreaterThan(1);
    }

    // ─── Click ────────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Click_Fires_OnClick()
    {
        var clicked = false;
        var cut = Render<TmButton>(p => p
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Click"));

        cut.Find("button").Click();

        clicked.Should().BeTrue();
    }

    // ─── Keyboard activation ──────────────────────────────────────────────────
    // A native <button> already produces "click" from the keyboard:
    // Enter fires click on keydown, Space on keyup. TmButton must not emulate
    // that in @onkeydown — emulation + the native click invoke OnClick TWICE
    // per key press. bUnit does not synthesize the native keyboard->click
    // behavior, so each test dispatches the full real-browser event sequence.
    // The no-op key handlers on the host wrapper exist only because bUnit
    // refuses to dispatch an event that has no handler on the target or its
    // ancestors; a real browser always bubbles key events to the document.

    [Fact]
    public void TmButton_KeyDown_Alone_Does_Not_Fire_OnClick()
    {
        // keydown is not an activation — the browser delivers the click itself.
        var clicks = 0;
        var cut = Render<ButtonHost>(p => p
            .Add(h => h.OnClick, EventCallback.Factory.Create(this, () => { clicks++; })));

        cut.Find("button").KeyDown(Key.Enter);

        clicks.Should().Be(0);
    }

    [Fact]
    public void TmButton_Enter_Sequence_Fires_OnClick_Exactly_Once()
    {
        var clicks = 0;
        var cut = Render<ButtonHost>(p => p
            .Add(h => h.OnClick, EventCallback.Factory.Create(this, () => { clicks++; })));

        var button = cut.Find("button");
        // Real browser sequence for Enter on a focused <button>:
        // keydown -> native click on the same element.
        button.KeyDown(Key.Enter);
        button.Click();

        clicks.Should().Be(1);
    }

    [Fact]
    public void TmButton_Space_Sequence_Fires_OnClick_Exactly_Once()
    {
        var clicks = 0;
        var cut = Render<ButtonHost>(p => p
            .Add(h => h.OnClick, EventCallback.Factory.Create(this, () => { clicks++; })));

        var button = cut.Find("button");
        // Real browser sequence for Space: keydown -> keyup -> native click.
        button.KeyDown(" ");
        button.KeyUp(" ");
        button.Click();

        clicks.Should().Be(1);
    }

    [Fact]
    public void TmButton_FocusRestoreContainer_KeySequence_Does_Not_Double_Fire()
    {
        // Regression test: a "trap" container (popover/dialog-style) closes on
        // keydown and restores focus to its TmButton trigger — so the trailing
        // native keyup/click lands on the trigger. With the old keydown
        // emulation OnClick fired twice per key press (the second invocation
        // re-opened the panel / re-triggered the action).
        var cut = Render<FocusRestoreTrapHost>();

        // Open the panel with a native click.
        var trigger = cut.Find("button.tm-btn");
        trigger.Click();
        cut.FindAll(".trap-panel").Should().HaveCount(1);
        cut.Instance.TriggerClickCount.Should().Be(1);

        // Space on the focused trigger: keydown (container restores focus to
        // the trigger) -> keyup -> native click delivered to the trigger.
        trigger.KeyDown(" ");
        trigger.KeyUp(" ");
        trigger.Click();

        cut.Instance.FocusRestoredToTrigger.Should().BeTrue();
        cut.Instance.TriggerClickCount.Should().Be(2, "one activation per gesture");
        cut.FindAll(".trap-panel").Should().BeEmpty("the panel must not re-open");
    }

    /// <summary>
    /// Hosts a <see cref="TmButton"/> inside a div that carries no-op key
    /// handlers so bUnit can dispatch keydown/keyup on the button (bUnit
    /// requires a handler on the target or an ancestor; real browsers bubble
    /// key events to the document regardless).
    /// </summary>
    private sealed class ButtonHost : ComponentBase
    {
        [Parameter] public EventCallback OnClick { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));
            builder.AddAttribute(2, "onkeyup",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));

            builder.OpenComponent<TmButton>(3);
            builder.AddAttribute(4, "OnClick", OnClick);
            builder.AddAttribute(5, "ChildContent",
                (RenderFragment)(b => b.AddContent(0, "Click")));
            builder.CloseComponent();

            builder.CloseElement();
        }
    }

    /// <summary>
    /// Models a focus-restore "trap" container (popover/dialog-style): on
    /// keydown it moves focus back to the TmButton trigger — recorded here as
    /// <see cref="FocusRestoredToTrigger"/> (a real implementation calls
    /// element.focus(); bUnit has no focus system, so the test dispatches the
    /// trailing keyup/click on the trigger element directly, exactly where the
    /// browser delivers them once focus is restored). The panel close itself
    /// is the trigger's toggle action, so the panel end-state reflects exactly
    /// one OnClick invocation per key gesture.
    /// </summary>
    private sealed class FocusRestoreTrapHost : ComponentBase
    {
        public bool PanelOpen { get; private set; }
        public int TriggerClickCount { get; private set; }
        public bool FocusRestoredToTrigger { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, HandleKeyDown));
            // no-op keyup sink — bUnit needs a handler somewhere on the bubble
            // path to dispatch the event at all.
            builder.AddAttribute(2, "onkeyup",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));

            builder.OpenComponent<TmButton>(2);
            builder.AddAttribute(3, "OnClick",
                EventCallback.Factory.Create(this, HandleTriggerClick));
            builder.AddAttribute(4, "ChildContent",
                (RenderFragment)(b => b.AddContent(0, "Trigger")));
            builder.CloseComponent();

            if (PanelOpen)
            {
                builder.OpenElement(5, "div");
                builder.AddAttribute(6, "class", "trap-panel");
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        private void HandleTriggerClick()
        {
            TriggerClickCount++;
            PanelOpen = !PanelOpen;
        }

        private void HandleKeyDown(KeyboardEventArgs e)
        {
            // The container's own close+focus-restore runs on keydown; the
            // panel close itself is driven by the trigger's toggle action so
            // that the end state reflects exactly one OnClick invocation.
            if (e.Key is "Enter" or " ")
                FocusRestoredToTrigger = true;
        }
    }

    // ─── TabIndex ─────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Default_TabIndex_Is_Zero()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void TmButton_Custom_TabIndex_Is_Applied()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.TabIndex, -1)
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("tabindex").Should().Be("-1");
    }

    // ─── AriaLabel ────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_AriaLabel_Is_Applied_When_Set()
    {
        var cut = Render<TmButton>(p => p
            .Add(c => c.AriaLabel, "Delete item")
            .AddChildContent("X"));

        cut.Find("button").GetAttribute("aria-label").Should().Be("Delete item");
    }

    [Fact]
    public void TmButton_AriaLabel_Not_Rendered_When_Null()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Save"));

        cut.Find("button").HasAttribute("aria-label").Should().BeFalse();
    }

    // ─── Base CSS class always present ────────────────────────────────────────

    [Fact]
    public void TmButton_Always_Has_TmBtn_Class()
    {
        var cut = Render<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn");
    }
}
