using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Guards what a keyboard and a screen reader get from a <c>TmDataTable</c> header row.
/// <para>
/// Two defects shipped up to 2.8.21, and both are of the kind that a rendered screenshot cannot show.
/// </para>
/// <list type="number">
/// <item>
/// <b><c>aria-sort="none"</c> on a column that cannot be sorted.</b> ARIA defines the attribute for a
/// <c>columnheader</c> whose column PARTICIPATES in the sort; <c>none</c> there means "sortable, not
/// currently sorted". Putting it on an ACTIONS column tells a screen-reader user that the column can be
/// sorted and simply is not — an affordance that does not exist. The attribute now appears only where
/// it is true, which is also the only state in which its absence is unambiguous.
/// </item>
/// <item>
/// <b>The pin toggle was a separate tab stop between every two headers.</b> It is a real
/// <c>&lt;button&gt;</c>, rendered for every visible column whenever <c>ShowColumnMenu</c> is on — the
/// default — so six columns cost eleven Tab presses to cross, and five of those stops painted nothing
/// until hover. The button is now out of the sequential order (<c>tabindex="-1"</c>) and the header it
/// belongs to answers <b>P</b> instead, so the function stays reachable at ONE stop per column.
/// Because pinning does not need a sortable column, a header that offers only the pin is a focus stop
/// too — otherwise taking the button out of the order would have made pinning unreachable on exactly
/// the columns that have no other affordance.
/// </item>
/// </list>
/// <para>
/// SPACE IS DELIBERATELY NOT AN ACTIVATION KEY here, and that is a decision of 2.8.9 this release did
/// not reverse — see <see cref="TmDataTableKeyboardSortTests.Space_DoesNotSort_SoItKeepsScrollingThePage"/>.
/// On a plain <c>&lt;th tabindex="0"&gt;</c> Space is the browser's "scroll one screen", and Blazor's
/// <c>:preventDefault</c> is bound when the handler is registered, not per event, so it cannot cancel
/// the scroll for one key without cancelling Tab as well. Accepting Space would therefore sort AND
/// throw the user a screen down.
/// </para>
/// </summary>
public class TmDataTableHeaderAccessibilityTests : LocalizationTestBase
{
    private sealed record HeaderPerson(string Name, int Age);

    private static List<HeaderPerson> People =>
    [
        new("Charlie", 30),
        new("Alice",   25),
        new("Bob",     35),
    ];

    /// <summary>A sortable column and a non-sortable one — the actions column of a real screen.</summary>
    private IRenderedComponent<TmDataTable<HeaderPerson>> RenderTable(bool showColumnMenu = true)
        => Render<TmDataTable<HeaderPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowColumnMenu, showColumnMenu);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<HeaderPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Field", (Func<HeaderPerson, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<HeaderPerson>>(5);
                b.AddAttribute(6, "Title", "Actions");
                b.AddAttribute(7, "PropertyName", "Actions");
                b.AddAttribute(8, "Sortable", false);
                b.CloseComponent();
            });
        });

    // ── aria-sort only where sorting exists ───────────────────────

    [Fact]
    public void ANonSortableHeader_DoesNotClaimToBeSortable()
        => RenderTable().Find("th[data-sortable='false']").GetAttribute("aria-sort").Should().BeNull(
            "aria-sort=\"none\" znamená „řaditelné, teď neseřazené“ — na sloupci AKCE je to nepravda, " +
            "kterou odečítač přečte jako nabídku");

    [Fact]
    public void ASortableHeader_StillAnnouncesItsSortState()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    // ── One tab stop per header, and the pin still reachable ──────

    [Fact]
    public void ThePinToggle_IsNotASeparateTabStop()
    {
        var cut = RenderTable();

        var pins = cut.FindAll(".tm-col-pin-btn");
        pins.Should().NotBeEmpty("bez vykresleného pinu by strážce netvrdil nic");
        pins.Should().AllSatisfy(pin => pin.GetAttribute("tabindex").Should().Be(
            "-1",
            "šest sloupců dávalo jedenáct zastávek, z toho pět nenamalovaných"));
    }

    /// <summary>
    /// The count is the point, so it is asserted as a count. Two columns with the menu on used to be
    /// four stops (two headers plus two pins) and one of the headers was not even focusable.
    /// </summary>
    [Fact]
    public void EveryHeaderIsExactlyOneTabStop()
    {
        var cut = RenderTable();

        var stops = cut.FindAll("thead tr:first-child th[tabindex='0']").Count
                    + cut.FindAll("thead tr:first-child [tabindex='0']:not(th)").Count;

        stops.Should().Be(2, "dva sloupce = dvě zastávky, ne čtyři");
    }

    [Fact]
    public void AHeaderThatOnlyOffersThePin_IsStillAFocusStop()
        => RenderTable().Find("th[data-sortable='false']").GetAttribute("tabindex").Should().Be(
            "0",
            "pin vypadl z pořadí, takže hlavička, na které je JEDINOU funkcí, musí být dosažitelná");

    [Fact]
    public void AHeaderWithNoAffordanceAtAll_IsNotAFocusStop()
        => RenderTable(showColumnMenu: false).Find("th[data-sortable='false']")
            .GetAttribute("tabindex").Should().BeNull(
                "hlavička, která nic nedělá, se do pořadí vracet nesmí");

    [Fact]
    public void P_CyclesThePin_FromTheHeader()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").ClassList.Should().NotContain("tm-col-pinned-left");

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "p" });

        cut.Find("th[data-sortable='true']").ClassList.Should().Contain("tm-col-pinned-left");
    }

    [Fact]
    public void P_CyclesThePin_OnANonSortableHeaderToo()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='false']").KeyDown(new KeyboardEventArgs { Key = "P" });

        cut.Find("th[data-sortable='false']").ClassList.Should().Contain("tm-col-pinned-left");
    }

    [Fact]
    public void P_DoesNothing_WhenTheColumnMenuIsOff()
    {
        var cut = RenderTable(showColumnMenu: false);

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "p" });

        cut.Find("th[data-sortable='true']").ClassList.Should().NotContain("tm-col-pinned-left");
    }

    [Fact]
    public void P_DoesNotSort()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "p" });

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
        cut.FindAll("tbody tr td:first-child").Select(cell => cell.TextContent.Trim())
            .Should().Equal("Charlie", "Alice", "Bob");
    }

    /// <summary>
    /// The shortcut has to be discoverable: a key that only the source code knows about is not a
    /// replacement for a tab stop.
    /// </summary>
    [Fact]
    public void TheHeaderAdvertisesTheShortcut()
        => RenderTable().Find("th[data-sortable='true']").GetAttribute("aria-keyshortcuts")
            .Should().Be("P");

    [Fact]
    public void TheHeaderDoesNotAdvertiseAShortcutItDoesNotOffer()
        => RenderTable(showColumnMenu: false).Find("th[data-sortable='true']")
            .GetAttribute("aria-keyshortcuts").Should().BeNull();

    // ── The shortcut must not reach a consumer's own controls ─────

    /// <summary>
    /// A header carrying a consumer's <c>HeaderTemplate</c> — the realistic case: a per-column filter
    /// box the user types into.
    /// </summary>
    private IRenderedComponent<TmDataTable<HeaderPerson>> RenderTableWithHeaderInput()
        => Render<TmDataTable<HeaderPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowColumnMenu, true);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<HeaderPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", true);
                b.AddAttribute(4, "Field", (Func<HeaderPerson, object?>)(x => x.Name));
                b.AddAttribute(5, "HeaderTemplate", (RenderFragment)(hb =>
                {
                    hb.OpenElement(0, "input");
                    hb.AddAttribute(1, "type", "text");
                    hb.AddAttribute(2, "data-testid", "consumer-header-filter");
                    hb.CloseElement();
                }));
                b.CloseComponent();
            });
        });

    /// <summary>
    /// The regression 2.8.22 introduced and 2.8.23 closes. <c>keydown</c> BUBBLES, so the single-key
    /// shortcut on the <c>&lt;th&gt;</c> fired for every letter typed into a control the consumer had
    /// put inside the header: typing "prague" into a filter box pinned and unpinned the column three
    /// times. WCAG 2.1.4 (Character Key Shortcuts, level A) allows a single-character shortcut only
    /// when it can be turned off, remapped, or is active solely while the component has focus — and
    /// focus on a DESCENDANT does not satisfy the third exception.
    /// </summary>
    [Fact]
    public void TypingIntoAConsumerHeaderControl_DoesNotPinTheColumn()
    {
        var cut = RenderTableWithHeaderInput();

        cut.Find("[data-testid='consumer-header-filter']").KeyDown(new KeyboardEventArgs { Key = "p" });

        cut.Find("th[data-sortable='true']").ClassList.Should().NotContain(
            "tm-col-pinned-left",
            "psaní písmene do consumerova vstupu nesmí připnout sloupec — keydown bublá");
    }

    /// <summary>
    /// Enter has the same shape and is older than the pin shortcut: it reached the sort handler from
    /// inside the template just as P did. Submitting a consumer's header filter must not re-sort the
    /// table underneath the user.
    /// </summary>
    [Fact]
    public void PressingEnterInAConsumerHeaderControl_DoesNotSort()
    {
        var cut = RenderTableWithHeaderInput();

        cut.Find("[data-testid='consumer-header-filter']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be(
            "none",
            "odeslání consumerova filtru v hlavičce nesmí přeřadit tabulku pod rukama");
    }

    /// <summary>
    /// The barrier must not COST the next keystroke. This is the defect the first attempt at the fix
    /// shipped in 2.8.23: the barrier set a flag that only the header's own handler cleared, so after a
    /// key was intercepted in the template the next genuine press on the header was spent clearing the
    /// flag and did nothing. A user typing a filter and then reaching for the header had to press twice.
    /// </summary>
    [Fact]
    public void AfterAKeyIsInterceptedInTheTemplate_TheNextKeyOnTheHeaderStillWorks()
    {
        var cut = RenderTableWithHeaderInput();

        cut.Find("[data-testid='consumer-header-filter']").KeyDown(new KeyboardEventArgs { Key = "p" });
        cut.Find("th[data-sortable='true']").ClassList.Should().NotContain("tm-col-pinned-left");

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "p" });

        cut.Find("th[data-sortable='true']").ClassList.Should().Contain(
            "tm-col-pinned-left",
            "bariéra smí zahodit klávesu z templatu, ne tu následující z hlavičky");
    }

    /// <summary>
    /// The same for Enter, because the two keys travel different branches of the handler and a
    /// stateful barrier would swallow whichever came first.
    /// </summary>
    [Fact]
    public void AfterAKeyIsInterceptedInTheTemplate_EnterOnTheHeaderStillSorts()
    {
        var cut = RenderTableWithHeaderInput();

        cut.Find("[data-testid='consumer-header-filter']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    /// <summary>
    /// The counterpart, so the fix is not "the header stopped answering the keyboard": pressing the
    /// keys on the HEADER ITSELF still works while a template is present.
    /// </summary>
    [Fact]
    public void TheHeaderItself_StillAnswersTheKeyboard_WhenATemplateIsPresent()
    {
        var cut = RenderTableWithHeaderInput();

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "p" });
        cut.Find("th[data-sortable='true']").ClassList.Should().Contain("tm-col-pinned-left");
    }
}
