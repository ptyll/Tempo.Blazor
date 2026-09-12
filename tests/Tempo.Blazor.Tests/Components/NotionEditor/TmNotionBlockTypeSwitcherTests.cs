using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>Tests for <see cref="TmNotionBlockTypeSwitcher"/>.</summary>
public class TmNotionBlockTypeSwitcherTests : LocalizationTestBase
{
    // The switcher panel (role=listbox) is not focusable itself — every key
    // event reaches its @onkeydown handler by bubbling from a focused item
    // <button>. Items are native buttons: the browser turns Enter into a click
    // on keydown (and Space on keyup), so a container-level Enter/Space case
    // selected a second time — the *highlighted* item, not necessarily the
    // focused one.

    [Fact]
    public void Switcher_ItemEnterKeySequence_SelectsExactlyOnce()
    {
        var selections = new List<BlockType>();
        var cut = Render<TmNotionBlockTypeSwitcher>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.CurrentType, BlockType.Paragraph)
            .Add(x => x.OnTypeChanged,
                EventCallback.Factory.Create<BlockType>(this, t => selections.Add(t))));

        // Move the highlight off the current type (idx 0 = Paragraph) so a
        // container-level Enter would pick Heading1 — a *different* item than
        // the one actually focused/clicked.
        cut.Find("[data-type-idx='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // Real sequence for Enter on a focused <button>: keydown -> click.
        var item = cut.Find("[data-type-idx='2']");
        item.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-type-idx='2']").Click();

        selections.Should().Equal(BlockType.Heading2);
    }

    [Fact]
    public void Switcher_ItemSpaceKeySequence_SelectsExactlyOnce()
    {
        var selections = new List<BlockType>();
        var cut = Render<SwitcherHost>(p => p
            .Add(h => h.OnTypeChanged,
                EventCallback.Factory.Create<BlockType>(this, t => selections.Add(t))));

        cut.Find("[data-type-idx='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // Real sequence for Space on a focused <button>: keydown -> keyup ->
        // native click.
        var item = cut.Find("[data-type-idx='2']");
        item.KeyDown(new KeyboardEventArgs { Key = " " });
        cut.Find("[data-type-idx='2']").KeyUp(new KeyboardEventArgs { Key = " " });
        cut.Find("[data-type-idx='2']").Click();

        selections.Should().Equal(BlockType.Heading2);
    }

    [Fact]
    public void Switcher_ArrowKeys_StillMoveHighlight()
    {
        // Arrows/Escape keep working through the container: the keydown bubbles
        // up from the focused item button.
        var cut = Render<TmNotionBlockTypeSwitcher>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.CurrentType, BlockType.Paragraph));

        cut.Find("[data-type-idx='0']")
            .ClassList.Should().Contain("tm-notion-type-switcher__item--highlighted");

        cut.Find("[data-type-idx='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.Find("[data-type-idx='1']")
            .ClassList.Should().Contain("tm-notion-type-switcher__item--highlighted");
    }

    [Fact]
    public void Switcher_EscapeOnItem_StillClosesPanel()
    {
        var closed = false;
        var cut = Render<TmNotionBlockTypeSwitcher>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.CurrentType, BlockType.Paragraph)
            .Add(x => x.OnClosed, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-type-idx='0']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().BeTrue();
    }

    /// <summary>
    /// Wraps the switcher in a div carrying no-op keydown/keyup sinks so bUnit
    /// can dispatch those events on the item buttons (bUnit requires a handler
    /// on the target or an ancestor; a real browser always bubbles key events).
    /// </summary>
    private sealed class SwitcherHost : ComponentBase
    {
        [Parameter] public EventCallback<BlockType> OnTypeChanged { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));
            builder.AddAttribute(2, "onkeyup",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));

            builder.OpenComponent<TmNotionBlockTypeSwitcher>(3);
            builder.AddAttribute(4, "Visible", true);
            builder.AddAttribute(5, "CurrentType", BlockType.Paragraph);
            builder.AddAttribute(6, "OnTypeChanged", OnTypeChanged);
            builder.CloseComponent();

            builder.CloseElement();
        }
    }
}
