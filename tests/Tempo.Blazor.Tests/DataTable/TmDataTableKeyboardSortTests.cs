using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Sorting has to be reachable without a mouse (WCAG 2.1.1 Keyboard).
/// <para>
/// Up to 2.8.25 the sort target was the <c>&lt;th tabindex="0"&gt;</c> itself, answering Enter in a
/// keydown handler — Space had to stay a non-key there, because on a plain element it still means
/// "scroll one screen" and Blazor cannot cancel that default for one key only. Since 2.8.26 the
/// activatable element is a real <c>&lt;button type="button" class="tm-th-sort"&gt;</c> inside the
/// <c>&lt;th&gt;</c>: Enter AND Space are native activation on a button, Space included — a button's
/// default for Space is "click", not "scroll", so sorting no longer throws the user a screen down.
/// The button wraps only the label and the sort icon; the pin toggle, the resize handle and a
/// consumer's <c>HeaderTemplate</c> stay siblings of it, because interactive content must not nest
/// inside a <c>&lt;button&gt;</c>.
/// </para>
/// <para>
/// The button carries no <c>@onclick</c> of its own: its native click bubbles to the <c>&lt;th&gt;</c>,
/// so keyboard and mouse take the same single path and a key can never sort twice. bUnit dispatches
/// the events a test asks for, so a keyboard activation is expressed as <c>.Click()</c> on the button —
/// which is exactly what a browser turns Enter and Space into on a focused <c>&lt;button&gt;</c>.
/// </para>
/// </summary>
public class TmDataTableKeyboardSortTests : LocalizationTestBase
{
    private sealed record KeyPerson(string Name, int Age);

    private static List<KeyPerson> People =>
    [
        new("Charlie", 30),
        new("Alice",   25),
        new("Bob",     35),
    ];

    private IRenderedComponent<TmDataTable<KeyPerson>> RenderTable(
        bool sortable = true,
        bool secondColumn = false,
        bool showColumnMenu = true)
        => Render<TmDataTable<KeyPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowColumnMenu, showColumnMenu);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<KeyPerson>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "PropertyName", "Name");
                b.AddAttribute(3, "Sortable", sortable);
                b.AddAttribute(4, "Field", (Func<KeyPerson, object?>)(x => x.Name));
                b.CloseComponent();

                if (secondColumn)
                {
                    b.OpenComponent<TmDataTableColumn<KeyPerson>>(5);
                    b.AddAttribute(6, "Title", "Age");
                    b.AddAttribute(7, "PropertyName", "Age");
                    b.AddAttribute(8, "Sortable", true);
                    b.AddAttribute(9, "Field", (Func<KeyPerson, object?>)(x => x.Age));
                    b.CloseComponent();
                }
            });
        });

    private static IReadOnlyList<string> Names(IRenderedComponent<TmDataTable<KeyPerson>> cut)
        => cut.FindAll("tbody tr").Select(r => r.QuerySelector("td")!.TextContent.Trim()).ToList();

    // ── The control inside the header ─────────────────────────────

    /// <summary>
    /// The contract this release promises: a sortable <c>&lt;th&gt;</c> contains
    /// <c>&lt;button type="button" class="tm-th-sort"&gt;</c>. <c>type="button"</c> is asserted on its
    /// own because inside a consumer's <c>&lt;form&gt;</c> the default <c>type="submit"</c> would
    /// submit the form on every sort.
    /// </summary>
    [Fact]
    public void SortableHeader_RendersAnInnerSortButton()
    {
        var cut = RenderTable();

        var button = cut.Find("th[data-sortable='true'] > button.tm-th-sort");

        button.GetAttribute("type").Should().Be("button",
            "v consumerově <form> by výchozí type=\"submit\" při každém řazení formulář odeslal");
    }

    /// <summary>
    /// A header with nothing to sort by does not offer the control — the button is the affordance,
    /// so its absence is what makes the column non-operable, not just a missing tabindex.
    /// </summary>
    [Fact]
    public void NonSortableHeader_RendersNoSortButton()
    {
        var cut = RenderTable(sortable: false);

        cut.Find("th[data-sortable='false']").QuerySelector("button.tm-th-sort").Should().BeNull();
    }

    /// <summary>
    /// The <c>&lt;button&gt;</c> is a native tab stop without a tabindex attribute — that is what
    /// makes it the element where Space activates without scrolling. The <c>&lt;th&gt;</c> itself is
    /// out of the order: two stops per column would be the noise 2.8.22 removed for the pin.
    /// </summary>
    [Fact]
    public void SortableHeader_ItsButtonIsTheFocusStop_NotTheTh()
    {
        var cut = RenderTable();

        var header = cut.Find("th[data-sortable='true']");
        header.GetAttribute("tabindex").Should().BeNull(
            "tlačítko uvnitř je nativní zastávka — tabindex na <th> by přidal druhou");
        header.QuerySelector("button.tm-th-sort").Should().NotBeNull(
            "bez vykresleného tlačítka by hlavička neměla zastávku vůbec");
    }

    // ── Operability ───────────────────────────────────────────────

    [Fact]
    public void ActivatingTheButton_SortsAscending()
    {
        var cut = RenderTable();

        // .Click() IS the keyboard path here: a browser turns Enter and Space on a focused <button>
        // into this very click.
        cut.Find("button.tm-th-sort").Click();

        Names(cut).Should().Equal("Alice", "Bob", "Charlie");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    /// <summary>
    /// Space sorts because the target is a button — and it cannot scroll the page, because the
    /// button consumes the key as activation. The assertion is on the markup fact that produces
    /// both behaviours (a real <c>&lt;button type="button"&gt;</c>), plus one dispatch proving the
    /// sort is NOT emulated on keydown: Space arrives exclusively through the native click, so
    /// there is no handler that could fire it while the page still scrolls.
    /// </summary>
    [Fact]
    public void Space_SortsThroughTheNativeClick_WhichCannotScroll()
    {
        var cut = RenderTable();

        var button = cut.Find("button.tm-th-sort");
        button.TagName.Should().Be("BUTTON",
            "jen na <button> je Space nativní aktivace — na prostém <th> zůstávalo „scroll o obrazovku“");

        button.KeyDown(new KeyboardEventArgs { Key = " " });

        Names(cut).Should().Equal("Charlie", "Alice", "Bob");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none",
            "řazení se NEemuluje na keydown — Space aktivuje přes nativní click, který button konzumuje");
    }

    /// <summary>
    /// The same guard for Enter: no keydown branch may fire the sort, or a real browser would sort
    /// twice — once from the keydown, once from the click the keydown synthesizes on a button.
    /// </summary>
    [Fact]
    public void KeydownEnter_DoesNotSort_TheClickDoes()
    {
        var cut = RenderTable();

        cut.Find("button.tm-th-sort").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none",
            "v prohlížeči keydown Enter na <button> sám vyrobí click — handler, který by řadil znovu, řadí dvakrát");

        cut.Find("button.tm-th-sort").Click();

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    [Fact]
    public void ActivatingTheButton_CyclesTheSameTriStateAsClickingTheHeader()
    {
        var cut = RenderTable();

        cut.Find("button.tm-th-sort").Click();
        cut.Find("button.tm-th-sort").Click();

        Names(cut).Should().Equal("Charlie", "Bob", "Alice");

        cut.Find("button.tm-th-sort").Click();

        Names(cut).Should().Equal("Charlie", "Alice", "Bob"); // back to the supplied order
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
    }

    /// <summary>
    /// Shift is the multi-sort modifier on the bubbled click — the browser reports it on the click
    /// Enter produces exactly as it does on a mouse click.
    /// </summary>
    [Fact]
    public void ShiftActivate_MultiSorts_LikeShiftClick()
    {
        var cut = RenderTable(secondColumn: true);

        cut.FindAll("button.tm-th-sort")[0].Click();
        cut.FindAll("button.tm-th-sort")[1].Click(new MouseEventArgs { ShiftKey = true });

        // Both columns are now sort keys, which only the multi-sort path produces.
        cut.FindAll("th[data-sortable='true']")
           .Select(h => h.GetAttribute("aria-sort"))
           .Should().Equal("ascending", "ascending");
    }

    /// <summary>
    /// Clicking the header outside the button still sorts — the mouse path is unchanged; the button
    /// added the keyboard one.
    /// </summary>
    [Fact]
    public void ClickingTheHeaderElsewhere_StillSorts()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").Click();

        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    [Fact]
    public void AnUnrelatedKey_DoesNotSort()
    {
        var cut = RenderTable();

        cut.Find("button.tm-th-sort").KeyDown(new KeyboardEventArgs { Key = "a" });

        Names(cut).Should().Equal("Charlie", "Alice", "Bob");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void Click_OnANonSortableHeader_DoesNothing()
    {
        var cut = RenderTable(sortable: false);

        cut.Find("th[data-sortable='false']").Click();

        Names(cut).Should().Equal("Charlie", "Alice", "Bob");
    }
}
