using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 9 data-table opt-ins: the edit-action footer spans every rendered column, and a custom
/// group header replaces the default label.
/// </summary>
public class TmDataTablePhase9Tests : LocalizationTestBase
{
    private sealed class Person
    {
        public string Name { get; set; } = "";
        public string Dept { get; set; } = "";
    }

    [Fact]
    public void Footer_edit_actions_span_every_rendered_column()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "phase9-footer");
            p.Add(c => c.Items, new[] { new Person { Name = "Ada", Dept = "Eng" } });
            p.Add(c => c.Editable, true);
            p.Add(c => c.Selectable, true);
            p.Add(c => c.EditActionsMode, EditActionsMode.Footer);
            p.Add(c => c.DetailTemplate, (RenderFragment<Person>)(_ => b => b.AddContent(0, "detail")));
            p.AddChildContent(b =>
            {
                Column(b, 0, "Name");
                Column(b, 10, "Dept");
            });
        });

        cut.Find("tbody tr").TriggerEvent("ondblclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        var footer = cut.Find("tr.tm-data-table-row--edit-footer");
        footer.QuerySelector("td")!.GetAttribute("colspan").Should().Be("5",
            "expander + checkbox + two data columns + the edit-action column");
        footer.TextContent.Should().Contain("Save");
        footer.TextContent.Should().Contain("Cancel");
        footer.QuerySelector("td.tm-data-table__edit-footer-actions").Should().BeNull(
            "flex must not take the footer td out of table layout, or colspan is ignored");
        footer.QuerySelector(".tm-data-table__edit-footer-actions").Should().NotBeNull();
    }

    [Fact]
    public void Text_mode_keeps_the_actions_in_the_row()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "phase9-text");
            p.Add(c => c.Items, new[] { new Person { Name = "Ada" } });
            p.Add(c => c.Editable, true);
            p.Add(c => c.EditActionsMode, EditActionsMode.Text);
            p.AddChildContent(b => Column(b, 0, "Name"));
        });

        cut.Find("tbody tr").TriggerEvent("ondblclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.FindAll("tr.tm-data-table-row--edit-footer").Should().BeEmpty();
        cut.Find("[data-testid='edit-commit']").TextContent.Should().Contain("Save");
    }

    [Fact]
    public async Task GroupHeaderTemplate_replaces_the_default_group_label()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "phase9-group");
            p.Add(c => c.Items, new[]
            {
                new Person { Name = "Ada", Dept = "Eng" },
                new Person { Name = "Bea", Dept = "Eng" },
            });
            p.Add(c => c.ShowGrouping, true);
            p.Add(c => c.GroupHeaderTemplate, (RenderFragment<DataGroup<Person>>)(group => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "phase9-dt-group");
                builder.AddContent(2, $"Dept {group.DisplayValue} ({group.Count})");
                builder.CloseElement();
            }));
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
                b.OpenComponent<TmDataTableColumn<Person>>(10);
                b.AddAttribute(11, "Title", "Dept");
                b.AddAttribute(12, "PropertyName", "Dept");
                b.AddAttribute(13, "Groupable", true);
                b.AddAttribute(14, "Field", (Func<Person, object?>)(x => x.Dept));
                b.CloseComponent();
            });
        });

        await cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Dept"));

        cut.Find(".phase9-dt-group").TextContent.Should().Contain("Dept Eng (2)");
        cut.FindAll(".tm-data-table-group-label").Should().BeEmpty();
    }

    private static void Column(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder b, int seq, string title)
    {
        b.OpenComponent<TmDataTableColumn<Person>>(seq);
        b.AddAttribute(seq + 1, "Title", title);
        b.AddAttribute(seq + 2, "Field", (Func<Person, object?>)(x => x.Name));
        b.AddAttribute(seq + 3, "Editable", true);
        b.AddAttribute(seq + 4, "EditTemplate", (RenderFragment<Person>)(x => child =>
            child.AddMarkupContent(0, $"<input value='{x.Name}' />")));
        b.CloseComponent();
    }
}
