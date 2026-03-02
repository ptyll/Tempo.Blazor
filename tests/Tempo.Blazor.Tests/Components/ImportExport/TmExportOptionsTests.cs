using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.ImportExport;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.ImportExport;

/// <summary>TDD tests for TmExportOptions.</summary>
public class TmExportOptionsTests : LocalizationTestBase
{
    private static TmExportFormat[] TestFormats() =>
    [
        new("csv", "CSV"),
        new("xlsx", "Excel"),
    ];

    private static TmExportEntity[] TestEntities() =>
    [
        new("users", "Users"),
        new("projects", "Projects"),
        new("tasks", "Tasks"),
    ];

    [Fact]
    public void TmExportOptions_Renders_Container()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities()));

        cut.Find(".tm-export-options").Should().NotBeNull();
    }

    [Fact]
    public void TmExportOptions_Renders_Title()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities())
            .Add(c => c.Title, "Export Data"));

        cut.Find(".tm-export-options-title").TextContent.Should().Contain("Export Data");
    }

    [Fact]
    public void TmExportOptions_Renders_Default_Title_From_Loc()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities()));

        cut.Find(".tm-export-options-title").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TmExportOptions_Renders_Entity_Checkboxes()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities()));

        cut.FindAll(".tm-export-options-entities .tm-checkbox-wrapper").Count.Should().Be(3);
    }

    [Fact]
    public void TmExportOptions_Renders_Format_Select()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities()));

        var options = cut.FindAll(".tm-export-options-format select option");
        options.Count.Should().Be(2);
    }

    [Fact]
    public void TmExportOptions_Export_Button_Fires_With_Selected_Data()
    {
        TmExportRequest? result = null;
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities())
            .Add(c => c.OnExport, EventCallback.Factory.Create<TmExportRequest>(this, r => result = r)));

        // Check the first entity checkbox
        cut.FindAll(".tm-export-options-entities .tm-checkbox-wrapper input")[0].Change(true);

        // Click export
        cut.Find(".tm-export-options-actions button.tm-btn-primary").Click();

        result.Should().NotBeNull();
        result!.Format.Should().Be("csv");
        result.SelectedEntityTypes.Should().Contain("users");
    }

    [Fact]
    public void TmExportOptions_Export_Disabled_When_No_Entities_Selected()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities()));

        var exportBtn = cut.Find(".tm-export-options-actions button.tm-btn-primary");
        exportBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmExportOptions_Applies_Custom_Class()
    {
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities())
            .Add(c => c.Class, "my-export"));

        cut.Find(".tm-export-options").ClassList.Should().Contain("my-export");
    }

    [Fact]
    public void TmExportOptions_Multiple_Entities_Can_Be_Selected()
    {
        TmExportRequest? result = null;
        var cut = RenderComponent<TmExportOptions>(p => p
            .Add(c => c.Formats, TestFormats())
            .Add(c => c.EntityTypes, TestEntities())
            .Add(c => c.OnExport, EventCallback.Factory.Create<TmExportRequest>(this, r => result = r)));

        var checkboxes = cut.FindAll(".tm-export-options-entities .tm-checkbox-wrapper input");
        checkboxes[0].Change(true);
        checkboxes[1].Change(true);

        cut.Find(".tm-export-options-actions button.tm-btn-primary").Click();

        result.Should().NotBeNull();
        result!.SelectedEntityTypes.Should().HaveCount(2);
        result.SelectedEntityTypes.Should().Contain("users");
        result.SelectedEntityTypes.Should().Contain("projects");
    }
}
