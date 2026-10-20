using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable.Snapshots;

/// <summary>
/// Deterministic default-markup scenarios. Each method renders a component with only the
/// parameters the scenario names (plus the library's required <c>ViewContext</c> on
/// <see cref="TmDataTable{TItem}"/>) and fixed data, so the same markup can be captured
/// against commit 259f900a and compared with the current tree.
/// </summary>
public static class DefaultMarkupScenarios
{
    /// <summary>Every scenario name, in the order the phase lists them.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "dt-basic",
        "dt-paged",
        "dt-selectable",
        "dt-grouping",
        "dt-filters",
        "dt-export",
        "dt-virtualized",
        "dt-cardmode",
        "dt-viewmanager",
        "mvl-table",
        "mvl-card",
        "mvl-list",
        "mvl-grouped",
        "filterbuilder-default",
        "pagination-default",
    ];

    /// <summary>Renders one scenario under an en-US culture and returns its raw markup.</summary>
    public static string Render(BunitContext context, string name)
    {
        using var culture = CultureScope.Use("en-US");
        {
            return name switch
            {
                "dt-basic" => RenderDataTable(context, pageSize: null, selectable: false).Markup,
                "dt-paged" => RenderDataTable(context, pageSize: 2, selectable: false).Markup,
                "dt-selectable" => RenderDataTable(context, pageSize: null, selectable: true).Markup,
                "dt-grouping" => RenderGrouping(context),
                "dt-filters" => RenderFilters(context),
                "dt-export" => RenderExport(context),
                "dt-virtualized" => RenderVirtualized(context),
                "dt-cardmode" => RenderCardMode(context),
                "dt-viewmanager" => RenderViewManager(context),
                "mvl-table" => RenderMultiView(context, ListViewMode.Table, grouped: false),
                "mvl-card" => RenderMultiView(context, ListViewMode.Card, grouped: false),
                "mvl-list" => RenderMultiView(context, ListViewMode.List, grouped: false),
                "mvl-grouped" => RenderMultiView(context, ListViewMode.Table, grouped: true),
                "filterbuilder-default" => RenderFilterBuilder(context),
                "pagination-default" => context.Render<TmPagination>().Markup,
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown markup scenario."),
            };
        }
    }

    private static IRenderedComponent<TmDataTable<SnapshotRow>> RenderDataTable(
        BunitContext context, int? pageSize, bool selectable)
        => context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            p.Add(c => c.ShowPagination, pageSize is not null);
            if (pageSize is int size)
            {
                p.Add(c => c.PageSize, size);
            }

            p.Add(c => c.Selectable, selectable);
            AddColumns(p, filterable: false, groupable: false);
        });

    private static string RenderGrouping(BunitContext context)
    {
        var cut = context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.ShowGrouping, true);
            p.Add(c => c.GroupsCollapsedByDefault, false);
            AddColumns(p, filterable: false, groupable: true);
        });

        cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Role")).GetAwaiter().GetResult();
        return cut.Markup;
    }

    private static string RenderFilters(BunitContext context)
    {
        var cut = context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            p.Add(c => c.ShowPagination, false);
            AddColumns(p, filterable: true, groupable: false);
        });

        cut.Find(".tm-filter-row input").Input("Alice");
        return cut.Markup;
    }

    private static string RenderExport(BunitContext context)
    {
        var cut = context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.ShowExport, true);
            AddColumns(p, filterable: false, groupable: false);
        });

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();
        return cut.Markup;
    }

    private static string RenderVirtualized(BunitContext context)
        => context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            // The phase names this Virtualize=true; the parameter that existed at 259f900a is ScrollMode.
            p.Add(c => c.ScrollMode, DataTableScrollMode.Virtualized);
            AddColumns(p, filterable: false, groupable: false);
        }).Markup;

    private static string RenderCardMode(BunitContext context)
        => context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.CardMode, true);
            AddColumns(p, filterable: false, groupable: false);
        }).Markup;

    private static string RenderViewManager(BunitContext context)
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>(
            [
                new DataTableView { Id = "view-all", Name = "All rows", Scope = ViewScope.Personal, IsDefault = true },
            ]));
        provider.GetDefaultViewAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DataTableView?>(null));

        var cut = context.Render<TmDataTable<SnapshotRow>>(p =>
        {
            p.Add(c => c.ViewContext, "snapshot");
            p.Add(c => c.Items, Rows());
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.ViewProvider, provider);
            p.Add(c => c.CurrentUserId, "snapshot-user");
            AddColumns(p, filterable: false, groupable: false);
        });

        cut.InvokeAsync(() => { }).GetAwaiter().GetResult();
        cut.Find(".tm-view-manager-toggle").Click();
        return cut.Markup;
    }

    private static string RenderMultiView(BunitContext context, ListViewMode mode, bool grouped)
    {
        var cut = context.Render<TmMultiViewList<SnapshotItem>>(p =>
        {
            p.Add(c => c.Items, Items());
            p.Add(c => c.ViewMode, mode);
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.TitleField, item => item.Title);
            p.Add(c => c.SubTitleField, item => item.Role);
            p.Add(c => c.StatusLabelField, item => item.Status);
            p.Add(c => c.DateField, item => new DateTimeOffset(item.Hired, TimeSpan.Zero));
            if (grouped)
            {
                p.Add(c => c.GroupableFields, GroupFields());
                p.Add(c => c.GroupsCollapsedByDefault, false);
            }
        });

        if (grouped)
        {
            cut.Find(".tm-mvl-group-btn").Click();
            cut.Find(".tm-mvl-group-dropdown input[type='checkbox']").Change(true);
        }

        return cut.Markup;
    }

    private static string RenderFilterBuilder(BunitContext context)
        => context.Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[]
            {
                new FilterDefinition { FieldName = "name", FieldLabel = "Name", FieldType = FilterFieldType.Text },
                new FilterDefinition { FieldName = "role", FieldLabel = "Role", FieldType = FilterFieldType.Text },
            })
            .Add(c => c.ActiveFilters, new[]
            {
                new ActiveFilter("name", "Name", FilterOperator.Contains, "Alice", "Alice"),
            })).Markup;

    private static void AddColumns(
        ComponentParameterCollectionBuilder<TmDataTable<SnapshotRow>> parameters,
        bool filterable,
        bool groupable)
    {
        parameters.AddChildContent(builder =>
        {
            builder.OpenComponent<TmDataTableColumn<SnapshotRow>>(0);
            builder.AddAttribute(1, "Title", "Name");
            builder.AddAttribute(2, "PropertyName", "Name");
            builder.AddAttribute(3, "Field", (Func<SnapshotRow, object?>)(row => row.Name));
            builder.AddAttribute(4, "Filterable", filterable);
            builder.CloseComponent();

            builder.OpenComponent<TmDataTableColumn<SnapshotRow>>(10);
            builder.AddAttribute(11, "Title", "Score");
            builder.AddAttribute(12, "PropertyName", "Score");
            builder.AddAttribute(13, "Field", (Func<SnapshotRow, object?>)(row => row.Score));
            builder.CloseComponent();

            builder.OpenComponent<TmDataTableColumn<SnapshotRow>>(20);
            builder.AddAttribute(21, "Title", "Hired");
            builder.AddAttribute(22, "PropertyName", "Hired");
            builder.AddAttribute(23, "Field", (Func<SnapshotRow, object?>)(row => row.Hired));
            builder.CloseComponent();

            if (groupable)
            {
                builder.OpenComponent<TmDataTableColumn<SnapshotRow>>(30);
                builder.AddAttribute(31, "Title", "Role");
                builder.AddAttribute(32, "PropertyName", "Role");
                builder.AddAttribute(33, "Field", (Func<SnapshotRow, object?>)(row => row.Role));
                builder.AddAttribute(34, "Groupable", true);
                builder.CloseComponent();
            }
        });
    }

    private static IReadOnlyList<GroupFieldDefinition<SnapshotItem>> GroupFields() =>
    [
        new GroupFieldDefinition<SnapshotItem>
        {
            FieldName = "Role",
            Label = "Role",
            FieldAccessor = item => item.Role,
        },
    ];

    private static List<SnapshotRow> Rows() =>
    [
        new("Alice", 30, new DateTime(2024, 1, 15), "Admin"),
        new("Bob", 25, new DateTime(2024, 1, 15), "User"),
        new("Carol", 35, new DateTime(2024, 1, 15), "Admin"),
        new("Dan", 28, new DateTime(2024, 1, 15), "User"),
        new("Eve", 41, new DateTime(2024, 1, 15), "Admin"),
    ];

    private static IReadOnlyList<SnapshotItem> Items() =>
        Rows().Select(row => new SnapshotItem(row.Name, row.Name, row.Role, "Active", row.Hired)).ToArray();

    /// <summary>Fixed row used by every data-table scenario.</summary>
    public sealed record SnapshotRow(string Name, int Score, DateTime Hired, string Role);

    /// <summary>Fixed item used by every multi-view scenario.</summary>
    public sealed record SnapshotItem(string Id, string Title, string Role, string Status, DateTime Hired);
}
