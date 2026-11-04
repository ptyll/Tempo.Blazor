using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Plan 3e96f6d8 phase 1: <c>TmSortableList&lt;TItem&gt;</c> — a generic list a reader can reorder.
/// These facts pin the ARIA model (<c>role="list"</c> of <c>listitem</c>s that name themselves a
/// "sortable item"), the keyboard model (the row is the only tab stop and emulates Enter on keydown /
/// Space on keyup, arrows move while grabbed, Escape rolls back, Alt+arrow moves one position without
/// grabbing), the single polite live region that narrates every step, the move menu that is the tap
/// alternative (WCAG 2.5.7), the <c>OnReorder(SortableReorderArgs)</c> commit contract with its
/// from/to indexes, the stable <c>@key</c> the <c>KeySelector</c> feeds, <c>Disabled</c>,
/// <c>ItemTemplate</c>, the two <c>HandleMode</c> shapes and the namespaced test ids.
/// </summary>
public class TmSortableListTests : LocalizationTestBase
{
    /// <summary>The row model used across these facts.</summary>
    public sealed record Row(string Id, string Title);

    private static IReadOnlyList<Row> Rows() =>
    [
        new("a", "Alpha"),
        new("b", "Bravo"),
        new("c", "Charlie")
    ];

    private static RenderFragment<Row> TitleTemplate() => row => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "row-title");
        builder.AddContent(2, row.Title);
        builder.CloseElement();
    };

    private IRenderedComponent<TmSortableList<Row>> RenderList(
        Action<ComponentParameterCollectionBuilder<TmSortableList<Row>>> configure)
        => Render<TmSortableList<Row>>(configure);

    private IRenderedComponent<TmSortableList<Row>> RenderDefault(
        Action<ComponentParameterCollectionBuilder<TmSortableList<Row>>>? extra = null)
        => RenderList(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemLabelSelector, (Func<Row, string>)(row => row.Title));
            p.Add(c => c.ItemTemplate, TitleTemplate());
            extra?.Invoke(p);
        });

    private static IReadOnlyList<string> Titles(IRenderedComponent<TmSortableList<Row>> cut)
        => cut.FindAll(".tm-sortable-list__item .row-title").Select(element => element.TextContent.Trim()).ToList();

    private static AngleSharp.Dom.IElement Item(IRenderedComponent<TmSortableList<Row>> cut, int index)
        => cut.FindAll(".tm-sortable-list__item")[index];

    private static string Status(IRenderedComponent<TmSortableList<Row>> cut)
        => cut.Find("[role='status']").TextContent.Trim();

    private static void Key(AngleSharp.Dom.IElement element, string key, bool alt = false)
        => element.KeyDown(new KeyboardEventArgs { Key = key, AltKey = alt });

    private static void Grab(IRenderedComponent<TmSortableList<Row>> cut, int index)
        => Item(cut, index).KeyDown(new KeyboardEventArgs { Key = "Enter" });

    private static void Drop(IRenderedComponent<TmSortableList<Row>> cut, int index)
        => Item(cut, index).KeyUp(new KeyboardEventArgs { Key = " " });

    private static EventCallback<SortableReorderArgs> Capture(Action<SortableReorderArgs> sink)
        => EventCallback.Factory.Create<SortableReorderArgs>(new object(), sink);

    // ---------- structure and ARIA ----------

    [Fact]
    public void RendersRoleListWithOneListItemPerEntry()
    {
        var cut = RenderDefault();

        cut.Find(".tm-sortable-list__items").GetAttribute("role").Should().Be("list");
        var items = cut.FindAll(".tm-sortable-list__item");
        items.Should().HaveCount(3);
        items.Should().OnlyContain(item => item.GetAttribute("role") == "listitem");
        Titles(cut).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void EveryItemNamesItselfASortableItem()
    {
        var cut = RenderDefault();

        cut.FindAll(".tm-sortable-list__item")
            .Select(item => item.GetAttribute("aria-roledescription"))
            .Should().Equal("sortable item", "sortable item", "sortable item");
    }

    [Fact]
    public void TheListIsNamedAndDescribedByTheLocalizedInstructions()
    {
        var cut = RenderDefault();

        var list = cut.Find(".tm-sortable-list__items");
        list.GetAttribute("aria-label").Should().Be("Sortable list");

        var describedBy = list.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{describedBy}").TextContent.Should().Contain("Enter or Space")
            .And.Contain("arrow keys", "the keyboard path is spelled out, not discovered by trial");

        // The row is the focusable element, so it carries the same description.
        Item(cut, 0).GetAttribute("aria-describedby").Should().Be(describedBy);
        Item(cut, 0).GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AHostAriaLabelReplacesTheDefaultName()
    {
        var cut = RenderDefault(p => p.Add(c => c.AriaLabel, "Review order"));

        cut.Find(".tm-sortable-list__items").GetAttribute("aria-label").Should().Be("Review order");
    }

    [Fact]
    public void TheRowIsTheOnlyTabStopOfThePair()
    {
        var cut = RenderDefault();

        Item(cut, 0).GetAttribute("tabindex").Should().Be("0", "the row is the keyboard grip");

        // The handle is a pointer affordance; a second tab stop per row would make the list a maze.
        var handle = cut.Find(".tm-sortable-list__handle");
        handle.TagName.Should().Be("BUTTON");
        handle.GetAttribute("type").Should().Be("button");
        handle.GetAttribute("tabindex").Should().Be("-1");
        handle.GetAttribute("aria-hidden").Should().Be("true");
        handle.GetAttribute("data-tm-sortable-handle").Should().NotBeNull();
    }

    [Fact]
    public void TemplateControlsKeepTheirOwnKeys()
    {
        // The row emulates Enter/Space, and Blazor's KeyboardEventArgs cannot tell which element sent
        // the event: without the fence a Space typed into a template input would grab the row AND type.
        SortableReorderArgs? raised = null;
        var cut = RenderList(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.OnReorder, Capture(args => raised = args));
            p.Add(c => c.ItemTemplate, (RenderFragment<Row>)(_ => builder =>
            {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "type", "text");
                builder.AddAttribute(2, "data-testid", "inner");
                builder.CloseElement();
            }));
        });

        var inner = cut.Find("[data-testid='inner']");

        // bUnit reports a fenced event as "no reachable handler": the content wrapper carries
        // @onkeydown:stopPropagation / @onkeyup:stopPropagation, so the row's emulating handler is never
        // reached — a browser behaves the same way, the control keeps the key for itself.
        var space = () => inner.KeyUp(new KeyboardEventArgs { Key = " " });
        space.Should().Throw<MissingEventHandlerException>(
            "Space typed into a template input must not grab the row");

        var enter = () => inner.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        enter.Should().Throw<MissingEventHandlerException>(
            "Enter on a template control must not grab the row");

        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("false",
            "a template control owns its own keys, so it never grabs the row");
        raised.Should().BeNull();
        Status(cut).Should().BeEmpty("nothing happened, so nothing is announced");
    }

    [Fact]
    public void DuplicateKeysFallBackToTheIndexInsteadOfThrowing()
    {
        var cut = RenderList(p =>
        {
            p.Add(c => c.Items, new[] { new Row("dup", "Alpha"), new Row("dup", "Bravo") });
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemTemplate, TitleTemplate());
        });

        Titles(cut).Should().Equal(
            new[] { "Alpha", "Bravo" },
            "a host that repeats a key still gets its rows instead of a Blazor diff exception");

        // The duplicate is what makes a KEYED diff ambiguous, so re-order the same list too.
        cut.Render(p =>
        {
            p.Add(c => c.Items, new[] { new Row("dup", "Bravo"), new Row("dup", "Alpha") });
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemTemplate, TitleTemplate());
        });

        Titles(cut).Should().Equal(new[] { "Bravo", "Alpha" }, "the index keys keep the reorder working");
    }

    [Fact]
    public void TheItemsCarryTheirKeySelectorValue()
    {
        var cut = RenderDefault();

        cut.FindAll(".tm-sortable-list__item")
            .Select(item => item.GetAttribute("data-sortable-key"))
            .Should().Equal("a", "b", "c");
    }

    /// <summary>
    /// A child component instance is the only observable that proves Blazor's KEYED diff moved the
    /// row instead of re-rendering the slot: the instance id travels with the item only when the list
    /// is keyed by <c>KeySelector</c>.
    /// </summary>
    [Fact]
    public void TheRowIdentityTravelsWithTheItem_SoTheListIsKeyed()
    {
        var probeIds = new Dictionary<string, string>();
        RenderFragment<Row> probe = row => builder =>
        {
            builder.OpenComponent<KeyProbe>(0);
            builder.AddAttribute(1, "Text", row.Title);
            builder.AddAttribute(2, "Register", (Action<string, string>)((title, id) => probeIds[title] = id));
            builder.CloseComponent();
        };

        var cut = RenderList(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemTemplate, probe);
        });

        Titles(cut).Should().Equal("Alpha", "Bravo", "Charlie");
        var alphaInstance = cut.FindAll(".row-title")[0].GetAttribute("data-probe");

        // Reversed: with @key the Alpha row keeps its instance, without it the first slot is reused.
        cut.Render(p =>
        {
            p.Add(c => c.Items, Rows().Reverse().ToList());
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemTemplate, probe);
        });

        Titles(cut).Should().Equal("Charlie", "Bravo", "Alpha");
        cut.FindAll(".row-title")[2].GetAttribute("data-probe").Should().Be(
            alphaInstance, "the keyed row kept its component instance through the reorder");
    }

    [Fact]
    public void WholeItemModeDropsTheHandleAndKeepsTheRowAsTheGrip()
    {
        var wholeItem = RenderList(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.HandleMode, SortableHandleMode.WholeItem);
            p.Add(c => c.ItemTemplate, TitleTemplate());
        });

        wholeItem.FindAll(".tm-sortable-list__handle").Should().BeEmpty("the whole row is the drag surface");
        Item(wholeItem, 0).GetAttribute("tabindex").Should().Be("0");
        Item(wholeItem, 0).GetAttribute("data-tm-sortable-grip").Should().NotBeNull();
        Item(wholeItem, 0).GetAttribute("aria-roledescription").Should().Be("sortable item");
        wholeItem.Find(".tm-sortable-list").GetAttribute("class").Should().Contain("tm-sortable-list--whole-item");
    }

    // ---------- the keyboard model ----------

    [Fact]
    public void GrabArrowDownDrop_CommitsOneReorder()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Grab(cut, 0);
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("true");
        Status(cut).Should().Be("Alpha picked up. Position 1 of 3.");

        Key(Item(cut, 0), "ArrowDown");
        Titles(cut).Should().Equal(new[] { "Bravo", "Alpha", "Charlie" },
            "the pending order moves the row on screen");
        Status(cut).Should().Be("Alpha moved to position 2 of 3.");

        Drop(cut, 1);

        raised.Should().NotBeNull();
        raised!.FromIndex.Should().Be(0);
        raised.ToIndex.Should().Be(1);
        raised.Source.Should().Be(SortableReorderSource.Keyboard);
        Status(cut).Should().Be("Alpha dropped at position 2 of 3.");
        cut.FindAll(".tm-sortable-list__item").Select(item => item.GetAttribute("data-grabbed"))
            .Should().Equal("false", "false", "false");
        Titles(cut).Should().Equal(new[] { "Alpha", "Bravo", "Charlie" },
            "an unbound host keeps its own order");
    }

    [Fact]
    public void ArrowKeysWithoutAGrabMoveNothing()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Key(Item(cut, 0), "ArrowDown");
        Key(Item(cut, 0), "ArrowUp");

        raised.Should().BeNull();
        Titles(cut).Should().Equal("Alpha", "Bravo", "Charlie");
        Status(cut).Should().BeEmpty("nothing happened, so nothing is announced");
    }

    [Fact]
    public void AltArrowMovesOnePositionWithoutGrabbing()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Key(Item(cut, 0), "ArrowDown", alt: true);

        raised.Should().NotBeNull();
        raised!.FromIndex.Should().Be(0);
        raised.ToIndex.Should().Be(1);
        raised.Source.Should().Be(SortableReorderSource.Keyboard);
        Status(cut).Should().Be("Alpha dropped at position 2 of 3.");
        cut.FindAll(".tm-sortable-list__item").Select(item => item.GetAttribute("data-grabbed"))
            .Should().OnlyContain(value => value == "false", "Alt+arrow is a single step, not a grab");
    }

    [Fact]
    public void AltArrowAtTheEdgeDoesNothingAndSaysWhy()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Key(Item(cut, 0), "ArrowUp", alt: true);

        raised.Should().BeNull();
        Status(cut).Should().Be("Alpha is already first.");

        Key(Item(cut, 2), "ArrowDown", alt: true);
        Status(cut).Should().Be("Charlie is already last.");
    }

    [Fact]
    public void EscapeRollsTheGrabBack()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Grab(cut, 0);
        Key(Item(cut, 0), "ArrowDown");
        Key(Item(cut, 0), "ArrowDown");
        Titles(cut).Should().Equal("Bravo", "Charlie", "Alpha");

        Key(Item(cut, 0), "Escape");

        raised.Should().BeNull();
        Titles(cut).Should().Equal(new[] { "Alpha", "Bravo", "Charlie" },
            "the pending order is rolled back");
        Status(cut).Should().Be("Move cancelled. Alpha stays at position 1 of 3.");
        cut.FindAll(".tm-sortable-list__item").Select(item => item.GetAttribute("data-grabbed"))
            .Should().OnlyContain(value => value == "false");
    }

    [Fact]
    public void TabReleasesTheGrabSoFocusIsNeverTrapped()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Grab(cut, 0);
        Key(Item(cut, 0), "ArrowDown");
        Titles(cut).Should().Equal("Bravo", "Alpha", "Charlie");

        Key(Item(cut, 0), "Tab");

        cut.Instance.GrabbedIndex.Should().BeNull(
            "Tab must release the grab, or the grabbed row is a WCAG 2.1.2 keyboard trap");
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("false");
        Titles(cut).Should().Equal(new[] { "Alpha", "Bravo", "Charlie" },
            "the grab is cancelled, so the host's order is painted again");
        Status(cut).Should().Be("Move cancelled. Alpha stays at position 1 of 3.");
        raised.Should().BeNull();
    }

    [Fact]
    public void HomeAndEndJumpWhileGrabbed()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        Grab(cut, 0);
        Key(Item(cut, 0), "End");
        Titles(cut).Should().Equal("Bravo", "Charlie", "Alpha");
        Key(Item(cut, 0), "Home");
        Titles(cut).Should().Equal("Alpha", "Bravo", "Charlie");

        Drop(cut, 0);
        raised.Should().BeNull("Home brought the row back to where the grab started");
        Status(cut).Should().Be("Alpha dropped at position 1 of 3.");
    }

    [Fact]
    public void MovingPastTheEdgeWhileGrabbedSaysWhy()
    {
        var cut = RenderDefault();

        Grab(cut, 0);
        Key(Item(cut, 0), "ArrowUp");

        Status(cut).Should().Be("Alpha is already first.");
    }

    [Fact]
    public void SpaceGrabsAndDropsInWholeItemMode()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderList(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.HandleMode, SortableHandleMode.WholeItem);
            p.Add(c => c.ItemLabelSelector, (Func<Row, string>)(row => row.Title));
            p.Add(c => c.ItemTemplate, TitleTemplate());
            p.Add(c => c.OnReorder, Capture(args => raised = args));
        });

        // Space is emulated on KEYUP, exactly where a native button fires its click.
        Item(cut, 0).KeyUp(new KeyboardEventArgs { Key = " " });
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("true");

        Key(Item(cut, 0), "ArrowDown");
        Titles(cut).Should().Equal("Bravo", "Alpha", "Charlie");

        Item(cut, 0).KeyUp(new KeyboardEventArgs { Key = " " });

        raised.Should().NotBeNull();
        raised!.FromIndex.Should().Be(0);
        raised.ToIndex.Should().Be(1);
    }

    [Fact]
    public void AHeldEnterDoesNotToggleTheGrabRepeatedly()
    {
        var cut = RenderDefault();

        Grab(cut, 0);
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("true");
        Item(cut, 0).KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("true", "auto-repeat must not drop the held row");
    }

    // ---------- the move menu (tap alternative) ----------

    [Fact]
    public void TheMoveMenuOffersTheFourNonDragMoves()
    {
        var cut = RenderDefault();

        cut.FindAll(".tm-sortable-list__move").Should().HaveCount(3, "one menu per row is the tap alternative");

        // The menu panel mounts only while it is open (a closed popover owns no DOM).
        cut.FindAll(".tm-context-menu__trigger")[1].Click();

        cut.FindAll(".tm-context-menu__item-label").Select(label => label.TextContent.Trim())
            .Should().Equal("Move up", "Move down", "Move to top", "Move to bottom");
    }

    [Fact]
    public void TheMenuTriggerNamesTheRowItMoves()
    {
        var cut = RenderDefault();

        var trigger = cut.FindAll(".tm-context-menu__trigger")[0];
        trigger.TextContent.Should().Contain("Move Alpha", "the trigger names its row, not only its icon");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
    }

    [Fact]
    public void AMenuMoveCommitsImmediately()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p => p.Add(c => c.OnReorder, Capture(args => raised = args)));

        cut.FindAll(".tm-context-menu__trigger")[0].Click();
        cut.FindAll(".tm-context-menu__item").Single(item => item.TextContent.Contains("Move to bottom")).Click();

        // The item closes its menu before running the handler (TmContextMenuItem yields once), so the
        // callback lands a moment after the click.
        cut.WaitForAssertion(() => raised.Should().NotBeNull());
        raised!.FromIndex.Should().Be(0);
        raised.ToIndex.Should().Be(2);
        raised.Source.Should().Be(SortableReorderSource.Menu);
        cut.WaitForAssertion(() => Status(cut).Should().Be("Alpha dropped at position 3 of 3."));
    }

    [Fact]
    public void TheFirstRowsMenuCannotMoveItUp()
    {
        var cut = RenderDefault();

        cut.FindAll(".tm-context-menu__trigger")[0].Click();

        cut.FindAll(".tm-context-menu__item")
            .Single(item => item.TextContent.Contains("Move up"))
            .HasAttribute("disabled").Should().BeTrue();
        cut.FindAll(".tm-context-menu__item")
            .Single(item => item.TextContent.Contains("Move down"))
            .HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TheMenuCanBeTurnedOff()
    {
        var cut = RenderDefault(p => p.Add(c => c.ShowMoveMenu, false));

        cut.FindAll(".tm-sortable-list__move").Should().BeEmpty();
        cut.FindAll(".tm-sortable-list__item").Should().HaveCount(3, "the keyboard path stays");
    }

    [Fact]
    public void TheMenuKeysNeverReachTheRowsGrabHandler()
    {
        var cut = RenderDefault();

        // The row handler would grab on Enter; the menu's own Enter must not (bUnit reports a missing
        // handler as MissingEventHandlerException only for the row, so the fence is observed as "no
        // grab happened").
        cut.FindAll(".tm-context-menu__trigger")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("false");
    }

    // ---------- Disabled ----------

    [Fact]
    public void DisabledRemovesEveryReorderPath()
    {
        SortableReorderArgs? raised = null;
        var cut = RenderDefault(p =>
        {
            p.Add(c => c.Disabled, true);
            p.Add(c => c.OnReorder, Capture(args => raised = args));
        });

        cut.Find(".tm-sortable-list").GetAttribute("data-disabled").Should().Be("true");
        cut.FindAll(".tm-sortable-list__move").Should().BeEmpty("no move menu when the list cannot move");
        Item(cut, 0).GetAttribute("tabindex").Should().BeNull("a read-only row is not a keyboard grip");

        Grab(cut, 0);
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("false");
        raised.Should().BeNull();
        Status(cut).Should().BeEmpty();
    }

    [Fact]
    public void TurningTheListReadOnlyMidGrabRollsThePendingOrderBack()
    {
        var cut = RenderDefault();

        Grab(cut, 0);
        Key(Item(cut, 0), "ArrowDown");
        Titles(cut).Should().Equal("Bravo", "Alpha", "Charlie");

        cut.Render(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemLabelSelector, (Func<Row, string>)(row => row.Title));
            p.Add(c => c.ItemTemplate, TitleTemplate());
            p.Add(c => c.Disabled, true);
        });

        Titles(cut).Should().Equal(new[] { "Alpha", "Bravo", "Charlie" },
            "a read-only list must not show the pending order of a grab that can no longer be dropped");
        Item(cut, 0).GetAttribute("data-grabbed").Should().Be("false");
    }

    [Fact]
    public void DisabledDropsTheDeadGripButKeepsTheExplanation()
    {
        var cut = RenderDefault(p => p.Add(c => c.Disabled, true));

        cut.FindAll(".tm-sortable-list__handle").Should().BeEmpty(
            "a grip that cannot move anything is a false affordance");
        cut.Find(".tm-sortable-list__items").GetAttribute("aria-describedby").Should().NotBeNullOrEmpty(
            "a read-only list still explains itself");
    }

    // ---------- empty, template, test ids ----------

    [Fact]
    public void EmptyItemsRenderTheEmptyContentAndKeepTheInstructions()
    {
        var cut = RenderList(p =>
        {
            p.Add(c => c.Items, Array.Empty<Row>());
            p.Add(c => c.EmptyContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "class", "empty-slot");
                builder.AddContent(2, "Nothing to order");
                builder.CloseElement();
            }));
        });

        cut.FindAll(".tm-sortable-list__item").Should().BeEmpty();
        cut.Find(".empty-slot").TextContent.Should().Be("Nothing to order");
        cut.Find(".tm-sortable-list__items").GetAttribute("aria-describedby").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WithoutAnEmptyContentTheLocalizedDefaultIsUsed()
    {
        var cut = RenderList(p => p.Add(c => c.Items, Array.Empty<Row>()));

        cut.Find(".tm-sortable-list__empty").TextContent.Trim().Should().Be("No items to reorder.");
    }

    [Fact]
    public void WithoutATemplateTheItemTextIsItsToString()
    {
        var cut = RenderList(p => p.Add(c => c.Items, new[] { new Row("a", "Alpha") }));

        cut.Find(".tm-sortable-list__content").TextContent.Should().Contain("Row");
    }

    [Fact]
    public void AHostClassIsAppendedToTheRoot()
    {
        var cut = RenderDefault(p => p.Add(c => c.Class, "my-list"));

        cut.Find(".tm-sortable-list").GetAttribute("class").Should().Contain("my-list");
    }

    [Fact]
    public void TestIdPrefixNamespacesTheInternalIds()
    {
        var cut = RenderDefault(p => p.Add(c => c.TestIdPrefix, "plan"));

        cut.Find("[data-testid='plan-sortable-list']").Should().NotBeNull();
        cut.Find("[data-testid='plan-sortable-list-status']").Should().NotBeNull();
    }

    [Fact]
    public void ThereIsExactlyOneLiveRegion()
    {
        var cut = RenderDefault();

        cut.FindAll("[role='status']").Should().ContainSingle("one live region, never one per row");
        cut.Find("[role='status']").GetAttribute("aria-live").Should().Be("polite");
        cut.Find("[role='status']").GetAttribute("aria-atomic").Should().Be("true");
    }

    [Fact]
    public void ARepeatedSentenceIsStillAnnounced()
    {
        var cut = RenderDefault();

        Key(Item(cut, 0), "ArrowUp", alt: true);
        var first = Status(cut);
        Key(Item(cut, 0), "ArrowUp", alt: true);
        var second = Status(cut);

        second.Should().StartWith(first, "the wording repeats, but the region must still see a mutation");
        second.Should().NotBe(first);
    }

    // ---------- localization ----------

    [Fact]
    public void AKeySelectorIsNotAName()
    {
        // A row's identity is usually a database key. Read aloud ("3f2a… picked up") it tells the reader
        // nothing, so the fallback name is the localized position label — never the key.
        var cut = RenderList(p =>
        {
            p.Add(c => c.Items, Rows());
            p.Add(c => c.KeySelector, (Func<Row, object>)(row => row.Id));
            p.Add(c => c.ItemTemplate, TitleTemplate());
        });

        Grab(cut, 0);

        Status(cut).Should().Be("Item 1 picked up. Position 1 of 3.");
        cut.FindAll(".tm-context-menu__trigger")[0].TextContent.Should().Contain("Move Item 1",
            "the move menu names the row too, so the key must not leak into that label either");
    }

    [Fact]
    public void AStringItemNamesItselfWithoutALabelSelector()
    {
        // A string IS its own text, so it is the one type whose ToString() is a usable name.
        var cut = Render<TmSortableList<string>>(p => p.Add(c => c.Items, new[] { "Alpha", "Bravo" }));

        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("[role='status']").TextContent.Trim().Should().Be("Alpha picked up. Position 1 of 2.");
    }

    [Fact]
    public void NoUserVisibleStringIsHardCoded()
    {
        // The Czech table proves the strings come from the localizer: an English literal would leak.
        Services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(BuildCzechLocalizer());
        var cut = RenderList(p => p.Add(c => c.Items, Rows()));

        cut.Find(".tm-sortable-list__items").GetAttribute("aria-label").Should().Be("Seznam k přesunutí");
        cut.FindAll(".tm-sortable-list__item")[0].GetAttribute("aria-roledescription")
            .Should().Be("přesunutelná položka");
    }

    /// <summary>Renders its own instance id, so a keyed diff is observable from the DOM.</summary>
    public sealed class KeyProbe : ComponentBase
    {
        private string? _id;

        [Parameter] public string? Text { get; set; }

        [Parameter] public Action<string, string>? Register { get; set; }

        protected override void OnInitialized() => _id = Guid.NewGuid().ToString("N");

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Register?.Invoke(Text ?? string.Empty, _id!);
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "row-title");
            builder.AddAttribute(2, "data-probe", _id);
            builder.AddContent(3, Text);
            builder.CloseElement();
        }
    }
}
