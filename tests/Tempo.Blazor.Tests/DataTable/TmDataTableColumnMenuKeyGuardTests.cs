using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 13 (found by the keyboard E2E): Alt+ArrowDown opens the column menu, and the same chord
/// scrolls the page in a real browser, which dismissed the menu as "anchor-hidden". The default is
/// cancelled in data-table.js; the table attaches that guard only while the menu is on.
/// </summary>
public class TmDataTableColumnMenuKeyGuardTests : LocalizationTestBase
{
    private sealed record Person(string Name);

    private IRenderedComponent<TmDataTable<Person>> RenderTable(ColumnMenuMode mode)
        => Render<TmDataTable<Person>>(p => p
            .Add(c => c.Items, new List<Person> { new("Ann") })
            .Add(c => c.ColumnMenuMode, mode)
            .AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "Name")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.Sortable, true)));

    [Fact]
    public void MenuMode_AttachesTheAltArrowDownGuard()
    {
        _ = RenderTable(ColumnMenuMode.Menu);

        JSInterop.VerifyInvoke("tmDataTable.attachColumnMenuKeys");
    }

    [Fact]
    public void DefaultMode_DoesNotAttachTheGuard()
    {
        _ = RenderTable(ColumnMenuMode.None);

        JSInterop.VerifyNotInvoke("tmDataTable.attachColumnMenuKeys");
    }
}
