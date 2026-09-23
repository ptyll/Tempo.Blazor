using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The import dialog's file chooser must be a real, keyboard-focusable button: the old
/// label-for-hidden-input pattern could never take focus, so keyboard users had no way to
/// pick a file. The button gets a NATIVE click listener (registered once per dialog lifetime
/// via <c>registerFilePickerTrigger</c>) that clicks the visually-hidden (not display:none)
/// InputFile synchronously inside the browser's own click event — a C#-mediated
/// <c>@onclick → JS interop</c> path would sit between the click and <c>input.click()</c>
/// and sever the user-activation chain WebKit requires (N204, review 2026-09-22).
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
    public void ImportDialog_FirstRender_RegistersNativeFilePickerTrigger_Once()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var register = module.SetupVoid("registerFilePickerTrigger", _ => true).SetVoidResult();

        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        register.Invocations.Should().HaveCount(1,
            "the native listener must be wired AHEAD of the first click — a click-time interop " +
            "round-trip would fall outside the user-activation window WebKit requires for input.click()");
    }

    [Fact]
    public void ImportDialog_Reopen_ReregistersTrigger_OnFreshDom()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var register = module.SetupVoid("registerFilePickerTrigger", _ => true).SetVoidResult();

        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.Render(p => p.Add(x => x.IsOpen, false));
        cut.Render(p => p.Add(x => x.IsOpen, true));

        register.Invocations.Should().HaveCount(2,
            "closing destroys the dialog DOM (@if IsOpen) — the reopened button is a NEW element " +
            "without the dataset marker, so the listener must be registered again");
    }

    [Fact]
    public void ImportDialog_TabRoundTrip_ReregistersTrigger_OnFreshDom()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var register = module.SetupVoid("registerFilePickerTrigger", _ => true).SetVoidResult();

        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        // Excel → Jira swaps the file area for the Jira form (@if / else-if in the dialog body):
        // the registered trigger button leaves the DOM even though the dialog stays open.
        cut.FindAll(".tm-gantt__import-tab")[2].Click();
        cut.FindAll("button[data-testid='import-choose-file']").Should().BeEmpty(
            "the Jira tab renders a different subtree — the file area and its button are destroyed");

        // Jira → Excel creates a NEW button element without the dataset.tmFilePickerRegistered
        // marker, so the native listener must be wired on it again.
        cut.FindAll(".tm-gantt__import-tab")[0].Click();

        var calls = register.Invocations["registerFilePickerTrigger"];
        calls.Should().HaveCount(2,
            "a C# 'registered once per open' flag can never observe the element a tab switch " +
            "recreates — registration must be re-attempted on every render and left for the " +
            "per-element dataset marker to dedupe");
        calls[1].Arguments[0].Should().NotBe(calls[0].Arguments[0],
            "the second call must target the freshly rendered trigger wrapper (@ref hands a new " +
            "ElementReference for the recreated element), not the destroyed one");
    }
}
