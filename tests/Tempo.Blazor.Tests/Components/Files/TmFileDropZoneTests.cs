using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>TDD tests for TmFileDropZone.</summary>
public class TmFileDropZoneTests : LocalizationTestBase
{
    [Fact]
    public void TmFileDropZone_Renders_DropZone()
    {
        var cut = RenderComponent<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_Has_DropArea()
    {
        var cut = RenderComponent<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__area").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_Has_FileInput()
    {
        var cut = RenderComponent<TmFileDropZone>();

        cut.FindAll("input[type=file]").Should().NotBeEmpty();
    }

    [Fact]
    public void TmFileDropZone_Disabled_Has_Disabled_Class()
    {
        var cut = RenderComponent<TmFileDropZone>(p => p
            .Add(c => c.Disabled, true));

        cut.Find(".tm-file-drop-zone").ClassList
            .Should().Contain("tm-file-drop-zone--disabled");
    }

    [Fact]
    public void TmFileDropZone_Multiple_Sets_Input_Multiple()
    {
        var cut = RenderComponent<TmFileDropZone>(p => p
            .Add(c => c.Multiple, true));

        cut.Find("input[type=file]").HasAttribute("multiple").Should().BeTrue();
    }

    [Fact]
    public void TmFileDropZone_Accept_Sets_Input_Accept()
    {
        var cut = RenderComponent<TmFileDropZone>(p => p
            .Add(c => c.Accept, "image/*,.pdf"));

        cut.Find("input[type=file]").GetAttribute("accept")
            .Should().Be("image/*,.pdf");
    }

    [Fact]
    public void TmFileDropZone_Shows_DefaultContent()
    {
        var cut = RenderComponent<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__hint").Should().NotBeNull();
        cut.Find(".tm-file-drop-zone__browse").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_Shows_CustomContent()
    {
        var cut = RenderComponent<TmFileDropZone>(p => p
            .AddChildContent("<span class='custom'>Upload here</span>"));

        cut.Find(".custom").TextContent.Should().Contain("Upload here");
        cut.FindAll(".tm-file-drop-zone__hint").Count.Should().Be(0);
    }

    [Fact]
    public void TmFileDropZone_InputCoversDropArea()
    {
        var cut = RenderComponent<TmFileDropZone>();

        // InputFile should have the overlay class inside the area
        cut.Find(".tm-file-drop-zone__area .tm-file-drop-zone__input").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_HasIcon()
    {
        var cut = RenderComponent<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__icon").Should().NotBeNull();
    }

    [Fact]
    public void TmFileDropZone_NoFilesInitially()
    {
        var cut = RenderComponent<TmFileDropZone>();

        cut.FindAll(".tm-file-drop-zone__file-item").Count.Should().Be(0);
    }
}
