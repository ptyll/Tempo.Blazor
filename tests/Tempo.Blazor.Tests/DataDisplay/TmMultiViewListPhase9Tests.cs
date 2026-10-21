using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Phase 9 opt-ins on <see cref="TmMultiViewList{TItem}"/>: stacked table cells, a badge variant
/// chosen by the host, and a custom group header used by both the list and the data table.
/// </summary>
public class TmMultiViewListPhase9Tests : LocalizationTestBase
{
    private sealed record Row(string Title, string SubTitle, string Status);

    private static Row[] Rows => [new("Ada", "Engineering", "Active"), new("Bea", "Sales", "Away")];

    [Fact]
    public void Inline_keeps_the_subtitle_in_its_own_column()
    {
        var cut = RenderList(TableCellLayout.Inline);

        cut.FindAll(".tm-mvl-cell-stack").Should().BeEmpty();
        cut.FindAll("thead th").Select(th => th.TextContent.Trim()).Should().Contain("Subtitle");
        cut.FindAll("tbody td").Select(td => td.TextContent.Trim()).Should().Contain("Engineering");
    }

    [Fact]
    public void Stacked_drops_the_subtitle_column_and_nests_it_under_the_title()
    {
        var cut = RenderList(TableCellLayout.Stacked);

        cut.FindAll("thead th").Select(th => th.TextContent.Trim()).Should().NotContain("Subtitle");
        cut.FindAll("td .tm-mvl-cell-stack").Should().HaveCount(Rows.Length);
        cut.FindAll("td.tm-mvl-cell-stack").Should().BeEmpty("flex must not take the td out of table layout");

        var cell = cut.Find("td .tm-mvl-cell-stack");
        cell.QuerySelector(".tm-mvl-cell-primary")!.TextContent.Should().Be("Ada");
        cell.QuerySelector(".tm-mvl-cell-secondary")!.TextContent.Should().Be("Engineering");
    }

    [Fact]
    public void Stacked_row_class_stays_on_the_row_not_the_cell()
    {
        var cut = RenderList(TableCellLayout.Stacked);

        cut.Find("tr.tm-mvl-row").ClassList.Should().NotContain("tm-mvl-cell-stack");
    }

    [Fact]
    public void StatusVariantField_paints_the_badge_the_host_asked_for()
    {
        var cut = Render<TmMultiViewList<Row>>(p => p
            .Add(c => c.Items, Rows)
            .Add(c => c.TitleField, (Func<Row, string>)(r => r.Title))
            .Add(c => c.StatusLabelField, (Func<Row, string?>)(r => r.Status))
            .Add(c => c.StatusVariantField, (Func<Row, BadgeVariant>)(r =>
                r.Status == "Active" ? BadgeVariant.Success : BadgeVariant.Warning)));

        var badges = cut.FindAll(".tm-mvl-status");
        badges[0].ClassList.Should().Contain("tm-badge-success");
        badges[1].ClassList.Should().Contain("tm-badge-warning");
    }

    [Fact]
    public void GroupHeaderTemplate_replaces_the_default_group_label()
    {
        var cut = Render<TmMultiViewList<Row>>(p => p
            .Add(c => c.Items, Rows)
            .Add(c => c.ViewMode, ListViewMode.Table)
            .Add(c => c.TitleField, (Func<Row, string>)(r => r.Title))
            .Add(c => c.GroupableFields, (IReadOnlyList<GroupFieldDefinition<Row>>)
            [
                new() { FieldName = "Status", Label = "Status", FieldAccessor = r => r.Status }
            ])
            .Add(c => c.GroupHeaderTemplate, (RenderFragment<DataGroup<Row>>)(group => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "phase9-group");
                builder.AddContent(2, $"Team {group.DisplayValue}");
                builder.CloseElement();
            })));

        cut.Find(".tm-mvl-group-btn").Click();
        cut.Find(".tm-mvl-group-option input").Change(true);

        cut.Find(".phase9-group").TextContent.Should().Contain("Team Active");
        cut.FindAll(".tm-mvl-group-label").Should().BeEmpty();
    }

    private IRenderedComponent<TmMultiViewList<Row>> RenderList(TableCellLayout layout)
        => Render<TmMultiViewList<Row>>(p => p
            .Add(c => c.Items, Rows)
            .Add(c => c.ViewMode, ListViewMode.Table)
            .Add(c => c.TitleField, (Func<Row, string>)(r => r.Title))
            .Add(c => c.SubTitleField, (Func<Row, string?>)(r => r.SubTitle))
            .Add(c => c.TableCellLayout, layout));
}
