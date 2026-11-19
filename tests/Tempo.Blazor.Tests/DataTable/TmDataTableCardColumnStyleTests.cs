using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Fáze 10a: a card cell must not keep the column's width. The cards switch on the table's OWN width (a
/// CSS container query), so the markup has to carry the column widths for the wide state — the cell of a
/// 72px column therefore still has its inline style, and the narrow-container card rule
/// (<c>DataTableCardsLayoutCssTests</c>) is what releases it. This pins the markup half of that contract:
/// dropping the inline style would silently collapse every column of the wide table to its content.
/// </summary>
public class TmDataTableCardColumnStyleTests : LocalizationTestBase
{
    private sealed record Row(string Id, string Name);

    private IRenderedComponent<TmDataTable<Row>> RenderCards(DataTableMobileLayout layout, bool cardMode = false) =>
        Render<TmDataTable<Row>>(p =>
        {
            p.Add(c => c.Items, new List<Row> { new("7", "Ada") });
            p.Add(c => c.MobileLayout, layout);
            p.Add(c => c.CardMode, cardMode);
            p.AddChildContent<TmDataTableColumn<Row>>(c => c
                .Add(x => x.Title, "ID").Add(x => x.Field, x => x.Id).Add(x => x.Width, "72px"));
            p.AddChildContent<TmDataTableColumn<Row>>(c => c
                .Add(x => x.Title, "Name").Add(x => x.Field, x => x.Name).Add(x => x.MinWidth, "10rem"));
        });

    [Theory]
    [InlineData(DataTableMobileLayout.Cards, false)]
    [InlineData(DataTableMobileLayout.None, true)]
    public void CardCells_KeepTheColumnWidthForTheWideContainer_TheNarrowRuleReleasesIt(DataTableMobileLayout layout, bool cardMode)
    {
        var cut = RenderCards(layout, cardMode);

        cut.Find("tbody td[data-label='ID']").GetAttribute("style").Should().Contain("width:72px");
        cut.Find("tbody td[data-label='Name']").GetAttribute("style").Should().Contain("min-width:10rem");
        cut.Find(".tm-data-table-wrapper--card").ClassList.Should().Contain("tm-data-table-wrapper--container",
            "the container the card rules (and the width release) are scoped to");
    }
}
