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
/// The sort target is the <c>&lt;th&gt;</c> itself, which is a plain element: it was clickable but not
/// focusable and had no key handler, so a keyboard user could not sort at all. It cannot simply be
/// wrapped in a <c>&lt;button&gt;</c> either — the header also carries the consumer's
/// <c>HeaderTemplate</c>, the pin toggle and the resize handle, and a button around those would nest
/// interactive elements. So the header itself becomes the focus stop and answers <b>Enter</b>.
/// </para>
/// <para>
/// Enter and not Space: on a plain <c>&lt;th&gt;</c> Space still means "scroll one screen", and Blazor
/// cannot cancel that default for one key only. See
/// <see cref="Space_DoesNotSort_SoItKeepsScrollingThePage"/>.
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

    // ── Focusability ──────────────────────────────────────────────

    [Fact]
    public void SortableHeader_IsAFocusStop()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").GetAttribute("tabindex").Should().Be("0");
    }

    /// <summary>
    /// Tabbing through headers that do nothing is noise, not access.
    /// </summary>
    /// <remarks>
    /// <c>ShowColumnMenu</c> is switched OFF here, and that is the whole content of the change made in
    /// 2.8.22: with the menu on, the header hosts the pin toggle, and since the toggle stopped being a
    /// tab stop of its own the header IS the way to reach it. "Does nothing" therefore has to be set up,
    /// not assumed from <c>Sortable="false"</c> alone — see
    /// <c>TmDataTableHeaderAccessibilityTests.AHeaderThatOnlyOffersThePin_IsStillAFocusStop</c>.
    /// </remarks>
    [Fact]
    public void NonSortableHeader_IsNotAFocusStop()
    {
        var cut = RenderTable(sortable: false, showColumnMenu: false);

        cut.Find("th[data-sortable='false']").GetAttribute("tabindex").Should().BeNull();
    }

    // ── Operability ───────────────────────────────────────────────

    [Fact]
    public void Enter_SortsAscending()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Names(cut).Should().Equal("Alice", "Bob", "Charlie");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("ascending");
    }

    /// <summary>
    /// Space must NOT sort — it stays the browser's "scroll one screen".
    /// </summary>
    /// <remarks>
    /// <para>
    /// This assertion is the reverse of what it was in 2.8.9, and the reversal is the fix. The header is
    /// a <c>&lt;th tabindex="0"&gt;</c>, not a <c>&lt;button&gt;</c>, so Space keeps its default action
    /// there. Accepting it as a sort key therefore did not replace the scroll, it ADDED to it: one press
    /// re-sorted the table and threw the user a screen down, off the result they had just asked for.
    /// </para>
    /// <para>
    /// Keeping Space and cancelling the scroll is not available from Blazor. <c>:preventDefault</c> is
    /// bound at handler registration, not per event, so it cannot look at the key — a static <c>true</c>
    /// would cancel Tab as well and trap the keyboard (WCAG 2.1.2), which is strictly worse than the
    /// annoyance it fixes.
    /// </para>
    /// <para>
    /// Nothing is lost in coverage terms: Enter sorts, so WCAG 2.1.1 Keyboard still holds, and Space is
    /// not an activation key for a <c>columnheader</c> in the first place.
    /// </para>
    /// </remarks>
    [Fact]
    public void Space_DoesNotSort_SoItKeepsScrollingThePage()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = " " });

        Names(cut).Should().Equal("Charlie", "Alice", "Bob");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void Enter_CyclesTheSameTriStateAsClicking()
    {
        var cut = RenderTable();

        var header = cut.Find("th[data-sortable='true']");
        header.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Names(cut).Should().Equal("Charlie", "Bob", "Alice");

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Names(cut).Should().Equal("Charlie", "Alice", "Bob"); // back to the supplied order
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void ShiftEnter_MultiSorts_LikeShiftClick()
    {
        var cut = RenderTable(secondColumn: true);

        var headers = cut.FindAll("th[data-sortable='true']");
        headers[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll("th[data-sortable='true']")[1]
           .KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        // Both columns are now sort keys, which only the multi-sort path produces.
        cut.FindAll("th[data-sortable='true']")
           .Select(h => h.GetAttribute("aria-sort"))
           .Should().Equal("ascending", "ascending");
    }

    [Fact]
    public void AnUnrelatedKey_DoesNotSort()
    {
        var cut = RenderTable();

        cut.Find("th[data-sortable='true']").KeyDown(new KeyboardEventArgs { Key = "a" });

        Names(cut).Should().Equal("Charlie", "Alice", "Bob");
        cut.Find("th[data-sortable='true']").GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void Enter_OnANonSortableHeader_DoesNothing()
    {
        var cut = RenderTable(sortable: false);

        cut.Find("th[data-sortable='false']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Names(cut).Should().Equal("Charlie", "Alice", "Bob");
    }
}
