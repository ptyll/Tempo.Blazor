using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// The additive TmFileDropZone hooks the Signing attachment step (redesign phase 4) composes:
/// <see cref="TmFileDropZone.InputAttributes"/> splats the host's accessibility plumbing onto the
/// internal <c>&lt;input type="file"&gt;</c> (the wrapper splat cannot reach it), and
/// <see cref="TmFileDropZone.ShowFileList"/> turns the zone's OWN picked-files list off when the
/// host presents the files itself — so the same file never shows twice.
/// </summary>
public class TmFileDropZoneAttributesTests : LocalizationTestBase
{
    [Fact]
    public void InputAttributes_SplatOntoTheNativeFileInput()
    {
        var cut = Render<TmFileDropZone>(p => p
            .Add(c => c.InputAttributes, new Dictionary<string, object>
            {
                ["aria-describedby"] = "step-description step-validation",
                ["aria-invalid"] = "true"
            }));

        var input = cut.Find("input[type='file']");
        input.GetAttribute("aria-describedby").Should().Be("step-description step-validation");
        input.GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void ShowFileList_DefaultTrue_RendersThePickedFilesList()
    {
        var cut = Render<TmFileDropZone>();

        PickFile(cut);

        cut.FindAll(".tm-file-drop-zone__file-list").Should().ContainSingle("the default keeps the built-in list");
    }

    [Fact]
    public void ShowFileList_False_SuppressesThePickedFilesList()
    {
        var cut = Render<TmFileDropZone>(p => p.Add(c => c.ShowFileList, false));

        PickFile(cut);

        cut.FindAll(".tm-file-drop-zone__file-list").Should().BeEmpty(
            "a host that renders its own file cards must not show the picked file twice");
    }

    private static void PickFile(IRenderedComponent<TmFileDropZone> cut)
    {
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("payload", "contract.pdf", contentType: "application/pdf"));
    }
}
