using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableBacklogTests : LocalizationTestBase
{
    [Fact]
    public void New_modes_keep_opt_in_defaults()
    {
        Assert.Equal(ColumnMenuMode.None, new Defaults().Menu);
        Assert.Equal(EditActionsMode.Icons, new Defaults().Edit);
        Assert.False(new Defaults().Export);
    }

    [Fact]
    public void Text_mode_shows_localized_save_label()
    {
        var item = new Person { Name = "Ann" };
        var cut = RenderTable(item, EditActionsMode.Text);
        cut.Find("tbody tr").TriggerEvent("ondblclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.Find("[data-testid='edit-commit']").TextContent.Should().Contain("Save");
        cut.Markup.Should().NotContain("tm-data-table-row--edit-footer");
    }

    [Fact]
    public void Icons_mode_does_not_add_footer_row()
    {
        var item = new Person { Name = "Ann" };
        var cut = RenderTable(item, EditActionsMode.Icons);
        cut.Find("tbody tr").TriggerEvent("ondblclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.Markup.Should().NotContain("tm-data-table-row--edit-footer");
        cut.Find("[data-testid='edit-commit']").TextContent.Should().NotContain("Save");
    }

    private IRenderedComponent<TmDataTable<Person>> RenderTable(Person item, EditActionsMode mode)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(x => x.ViewContext, "backlog");
            p.Add(x => x.Items, new[] { item });
            p.Add(x => x.Editable, true);
            p.Add(x => x.EditActionsMode, mode);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Field", (Func<Person, object?>)(x => x.Name));
                b.AddAttribute(3, "Editable", true);
                b.AddAttribute(4, "EditTemplate", (Microsoft.AspNetCore.Components.RenderFragment<Person>)(x => child => child.AddMarkupContent(0, $"<input value='{x.Name}' />")));
                b.CloseComponent();
            });
        });

    private sealed class Person { public string Name { get; set; } = ""; }
    private sealed class Defaults { public ColumnMenuMode Menu { get; } = ColumnMenuMode.None; public EditActionsMode Edit { get; } = EditActionsMode.Icons; public bool Export { get; } }
}
