using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The import dialog's file chooser must be a real, keyboard-focusable button: the old
/// label-for-hidden-input pattern could never take focus, so keyboard users had no way to
/// pick a file. The button gets a NATIVE click listener (registration re-attempted on EVERY
/// render via <c>registerFilePickerTrigger</c> and deduplicated per element by the JS
/// <c>dataset.tmFilePickerRegistered</c> marker — tab switches and reopen destroy/recreate the
/// trigger, which no C# flag could observe) that clicks the visually-hidden (not display:none)
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
    public void ImportDialog_FirstRender_RegistersNativeFilePickerTrigger_AheadOfFirstClick()
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

    // ── Dialog semantics (N153) ────────────────────────────────

    [Fact]
    public void Dialog_HasDialogRoleAriaModalAndLabelledBy()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        var dialog = cut.Find(".tm-gantt__import-dialog");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{labelledBy}").TextContent.Trim()
            .Should().Be(cut.Find(".tm-gantt__dialog-title").TextContent.Trim());
    }

    [Fact]
    public void CloseButton_HasAccessibleName()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        // The close button is icon-only (TmIcon is aria-hidden) — it must carry an aria-label.
        cut.Find(".tm-gantt__dialog-close").GetAttribute("aria-label")
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ImportError_HasAlertRole()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        // Drive the component into its error state: an Excel import without the GanttXlsx
        // package (or with an unreadable stream) surfaces _errorMessage.
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
           .UploadFiles(InputFileContent.CreateFromBinary(new byte[] { 1, 2, 3 }, "tasks.xlsx"));
        cut.FindAll(".tm-gantt__dialog-actions button")[0].Click();

        cut.Find(".tm-gantt-task-panel__error").GetAttribute("role").Should().Be("alert",
            "the import error must be announced — same convention as TmGanttTaskPanel's error surface");
    }

    [Fact]
    public void SelectedFileName_IsLiveRegion()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        // The element is persistent (not conditionally mounted) so assistive tech registers the
        // live region before the first file lands in it.
        cut.Find(".tm-gantt__import-file-name").GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public async Task ImportDialog_Escape_ClosesDialog()
    {
        // Carry-forward from 20C review: a modal dialog must close on Escape (APG contract).
        // The shared focus-trap module delivers document-level Escape via this JSInvokable.
        var closed = false;
        var cut = Render<TmGanttImportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnClose, () => closed = true));

        await cut.InvokeAsync(() => cut.Instance.HandleFocusTrapEscapeAsync());

        closed.Should().BeTrue();
    }

    [Fact]
    public void ImportDialog_FocusTrap_ActivatesWithEscapeHandling()
    {
        var focusTrap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        var activate = focusTrap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        var calls = activate.Invocations["activate"];
        calls.Should().HaveCount(1);
        calls[0].Arguments.Should().HaveCount(4,
            "activate(element, id, escapeHandler, closeOnEscape) — an escape handler and the " +
            "closeOnEscape flag must reach the shared focus-trap module");
        calls[0].Arguments[3].Should().Be(true);
    }
}
