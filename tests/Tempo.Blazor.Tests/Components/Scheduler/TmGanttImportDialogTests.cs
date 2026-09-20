using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The import dialog's file chooser must be a real, keyboard-focusable button: the old
/// label-for-hidden-input pattern could never take focus, so keyboard users had no way to
/// pick a file. The button delegates to the collocated JS module, which clicks the
/// visually-hidden (not display:none) InputFile inside the same user gesture.
/// </summary>
public class TmGanttImportDialogTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Scheduler/TmGanttImportDialog.razor.js";

    [Fact]
    public void ImportDialog_RendersButton_NotLabel()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        var button = cut.Find("button[data-testid='import-choose-file']");
        button.TextContent.Should().Contain("Choose file");

        cut.FindAll(".tm-gantt__import-file-area label").Should().BeEmpty(
            "a <label> can never receive keyboard focus — the chooser affordance is a real button");
    }

    [Fact]
    public void ImportDialog_InputFile_IsVisuallyHidden_NotDisplayNone()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        var input = cut.Find("input.tm-gantt__import-file-input");
        // aria-hidden + tabindex=-1: present in the DOM for programmatic click() but never
        // announced and never tabbed to — the button is the interactive surface.
        input.GetAttribute("aria-hidden").Should().Be("true");
        input.GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void ImportDialog_ButtonClick_InvokesOpenFilePicker()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var open = module.SetupVoid("openFilePicker", _ => true).SetVoidResult();

        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.Find("button[data-testid='import-choose-file']").Click();

        open.Invocations.Should().HaveCount(1, "the button must click the hidden input via JS");
    }
}
