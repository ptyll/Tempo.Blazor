using System.Text.Json;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// F-04/F-05: the demo API round-trips record field values as <see cref="JsonElement"/>. Select,
/// Status and MultiSelect values must render as pills/chips in the record detail AND the table
/// cells — a JsonElement is neither a string nor an IEnumerable, so the read renderers used to
/// come up empty while the same record showed values elsewhere.
/// </summary>
public class TmNotionDbJsonElementValuesTests : LocalizationTestBase
{
    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    [Fact]
    public void PropertyValue_RendersPill_ForJsonElementString_Select()
    {
        var field = SelectField("Status", DatabaseFieldType.Select, "In Progress", "blue");
        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, Json("\"In Progress\"")));

        cut.FindAll("[data-testid=notion-db-value-pill]").Should().HaveCount(1)
            .And.Subject.Single().TextContent.Should().Contain("In Progress");
    }

    [Fact]
    public void PropertyValue_RendersPill_ForJsonElementString_Status()
    {
        var field = StatusField("Status", "Done", "green");
        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, Json("\"Done\"")));

        cut.FindAll("[data-testid=notion-db-value-pill]").Should().HaveCount(1)
            .And.Subject.Single().TextContent.Should().Contain("Done");
    }

    [Fact]
    public void PropertyValue_RendersPills_ForJsonElementArray_MultiSelect()
    {
        var field = SelectField("Tags", DatabaseFieldType.MultiSelect, "tag-frontend", "purple", "bug", "red");
        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, Json("[\"tag-frontend\",\"bug\"]")));

        cut.FindAll("[data-testid=notion-db-value-pill]").Should().HaveCount(2);
        cut.Markup.Should().Contain("tag-frontend").And.Contain("bug");
    }

    [Fact]
    public void TableView_Cell_RendersSelectChip_ForJsonElementValue()
    {
        var (fields, records) = SeedSelectRecord(DatabaseFieldType.Select, Json("\"In Progress\""));
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.ReadOnly, true));

        cut.FindAll(".tm-dbc__chip").Should().HaveCountGreaterThanOrEqualTo(1);
        cut.Markup.Should().Contain("In Progress");
    }

    [Fact]
    public void TableView_Cell_RendersStatusChip_ForJsonElementValue()
    {
        var (fields, records) = SeedSelectRecord(DatabaseFieldType.Status, Json("\"In Progress\""));
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.ReadOnly, true));

        cut.Markup.Should().Contain("In Progress");
    }

    [Fact]
    public void TableView_Cell_RendersMultiSelectChips_InReadMode()
    {
        var (fields, records) = SeedSelectRecord(
            DatabaseFieldType.MultiSelect, Json("[\"tag-frontend\",\"bug\"]"), "tag-frontend", "bug");
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.ReadOnly, true));

        cut.FindAll(".tm-dbc__chip").Should().HaveCount(2);
        cut.Markup.Should().Contain("tag-frontend").And.Contain("bug");
    }

    [Fact]
    public void TableView_Cell_RendersMultiSelectChips_ForObjectArray()
    {
        // The demo client normalizes a JSON array to object[] — not string[], not JsonElement.
        var (fields, records) = SeedSelectRecord(
            DatabaseFieldType.MultiSelect, new object[] { "tag-frontend", "tag-backend" },
            "tag-frontend", "tag-backend");
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.ReadOnly, true));

        cut.FindAll(".tm-dbc__chip").Should().HaveCount(2);
        cut.Markup.Should().Contain("tag-frontend").And.Contain("tag-backend");
    }

    private static DatabaseField SelectField(string name, DatabaseFieldType type, params string[] optionNames)
    {
        var options = optionNames
            .Select((n, i) => new SelectFieldOption($"opt-{i + 1}", n, i % 2 == 0 ? "blue" : "purple"))
            .ToList();
        return new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            Config = new SelectFieldConfig { Options = options }
        };
    }

    private static DatabaseField StatusField(string name, string optionName, string color)
    {
        var option = new SelectFieldOption("opt-1", optionName, color);
        return new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = DatabaseFieldType.Status,
            Config = new StatusFieldConfig
            {
                Groups =
                [
                    new StatusGroup("To-do", "gray", [option])
                ]
            }
        };
    }

    private static (DatabaseField[] Fields, DatabaseRecord[] Records) SeedSelectRecord(
        DatabaseFieldType type, object? value, params string[] optionNames)
    {
        var names = optionNames.Length > 0 ? optionNames : ["In Progress"];
        var field = type == DatabaseFieldType.Status
            ? StatusField(type.ToString(), names[0], "blue")
            : SelectField(type.ToString(), type, names);
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [field.Id.ToString()] = value }
        };
        return ([field], [record]);
    }
}
