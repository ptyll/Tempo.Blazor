using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.ImportExport;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.ImportExport;

/// <summary>TDD tests for TmImportPreview.</summary>
public class TmImportPreviewTests : LocalizationTestBase
{
    [Fact]
    public void TmImportPreview_Renders_Container()
    {
        var cut = RenderComponent<TmImportPreview>();

        cut.Find(".tm-import-preview").Should().NotBeNull();
    }

    [Fact]
    public void TmImportPreview_Renders_Title()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.Title, "Import Users"));

        cut.Find(".tm-import-preview-title").TextContent.Should().Contain("Import Users");
    }

    [Fact]
    public void TmImportPreview_Renders_Description()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.Description, "Review the data below."));

        cut.Find(".tm-import-preview-description").TextContent.Should().Contain("Review the data below.");
    }

    [Fact]
    public void TmImportPreview_Renders_ChildContent()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .AddChildContent("<table><tr><td>Row1</td></tr></table>"));

        cut.Find(".tm-import-preview-content").InnerHtml.Should().Contain("<table>");
    }

    [Fact]
    public void TmImportPreview_Confirm_Button_Fires_OnConfirm()
    {
        bool confirmed = false;
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true)));

        cut.Find(".tm-import-preview-actions button.tm-btn-primary").Click();

        confirmed.Should().BeTrue();
    }

    [Fact]
    public void TmImportPreview_Cancel_Button_Fires_OnCancel()
    {
        bool cancelled = false;
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        cut.Find(".tm-import-preview-actions button.tm-btn-secondary").Click();

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void TmImportPreview_Custom_Button_Texts()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.ConfirmText, "Start Import")
            .Add(c => c.CancelText, "Go Back"));

        var buttons = cut.FindAll(".tm-import-preview-actions button");
        buttons.Should().Contain(b => b.TextContent.Contains("Go Back"));
        buttons.Should().Contain(b => b.TextContent.Contains("Start Import"));
    }

    [Fact]
    public void TmImportPreview_IsLoading_Disables_Confirm()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.IsLoading, true));

        var confirmBtn = cut.Find(".tm-import-preview-actions button.tm-btn-primary");
        confirmBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmImportPreview_Applies_Custom_Class()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.Class, "my-preview"));

        cut.Find(".tm-import-preview").ClassList.Should().Contain("my-preview");
    }

    [Fact]
    public void TmImportPreview_Hides_Header_When_No_Title_Or_Description()
    {
        var cut = RenderComponent<TmImportPreview>();

        cut.FindAll(".tm-import-preview-header").Count.Should().Be(0);
    }

    [Fact]
    public void TmImportPreview_Shows_Header_When_Title_Set()
    {
        var cut = RenderComponent<TmImportPreview>(p => p
            .Add(c => c.Title, "Preview"));

        cut.FindAll(".tm-import-preview-header").Count.Should().Be(1);
    }
}
