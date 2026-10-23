using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// Decision TOOLBAR-FILE-BUTTON: <see cref="TmToolbarFileButton"/> is a toolbar button that opens a file chooser. On the bar
/// it is a <see cref="TmToolbarButton"/> (same classes, labels, priority and rank); in the "More" menu it is an ordinary
/// entry whose activation clicks the SAME hidden <c>InputFile</c>. The input lives OUTSIDE the collapse wrapper, so a
/// collapse / expand never remounts it (the remount is what made the toolbar unstable in the earlier attempt).
/// Like the overflow tests, the fit count is driven directly - the real measurement is the E2E lane.
/// </summary>
public class TmToolbarFileButtonTests : LocalizationTestBase
{
    private const string ToolbarModule = "./_content/Tempo.Blazor/js/tm-toolbar.js";
    private const string FileModule = "./_content/Tempo.Blazor/Components/Toolbar/TmToolbarFileButton.razor.js";

    private BunitJSModuleInterop SetupToolbarModule()
    {
        var module = JSInterop.SetupModule(ToolbarModule);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        return module;
    }

    private BunitJSModuleInterop SetupFileModule()
    {
        var module = JSInterop.SetupModule(FileModule);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    private static RenderFragment FileButton(
        string text = "Import",
        ToolbarButtonPriority priority = ToolbarButtonPriority.Secondary,
        string? icon = "upload",
        string? tooltip = null,
        string? accept = ".json",
        bool multiple = false,
        bool disabled = false,
        EventCallback<InputFileChangeEventArgs> onChange = default,
        string? dataTestId = null,
        string? cssClass = null) => builder =>
    {
        builder.OpenComponent<TmToolbarFileButton>(0);
        builder.AddComponentParameter(1, nameof(TmToolbarFileButton.Text), text);
        builder.AddComponentParameter(2, nameof(TmToolbarFileButton.Priority), priority);
        builder.AddComponentParameter(3, nameof(TmToolbarFileButton.Icon), icon);
        builder.AddComponentParameter(4, nameof(TmToolbarFileButton.Tooltip), tooltip);
        builder.AddComponentParameter(5, nameof(TmToolbarFileButton.Accept), accept);
        builder.AddComponentParameter(6, nameof(TmToolbarFileButton.Multiple), multiple);
        builder.AddComponentParameter(7, nameof(TmToolbarFileButton.Disabled), disabled);
        builder.AddComponentParameter(8, nameof(TmToolbarFileButton.OnChange), onChange);
        builder.AddComponentParameter(9, nameof(TmToolbarFileButton.DataTestId), dataTestId);
        builder.AddComponentParameter(10, nameof(TmToolbarFileButton.Class), cssClass);
        builder.CloseComponent();
    };

    private static RenderFragment Buttons(params (string Text, ToolbarButtonPriority Priority)[] specs) => builder =>
    {
        for (var i = 0; i < specs.Length; i++)
        {
            builder.OpenComponent<TmToolbarButton>(i);
            builder.AddComponentParameter(0, nameof(TmToolbarButton.Text), specs[i].Text);
            builder.AddComponentParameter(1, nameof(TmToolbarButton.Icon), "plus");
            builder.AddComponentParameter(2, nameof(TmToolbarButton.Priority), specs[i].Priority);
            builder.SetKey(specs[i].Text);
            builder.CloseComponent();
        }
    };

    private IRenderedComponent<TmToolbar> RenderToolbar(RenderFragment content, ToolbarOverflow overflow = ToolbarOverflow.Menu)
        => Render<TmToolbar>(p => p
            .Add(c => c.Overflow, overflow)
            .Add(c => c.ChildContent, content)
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop)));

    private static void OpenMenu(IRenderedComponent<TmToolbar> cut) => cut.Find("button.tm-toolbar-more").Click();

    private static IReadOnlyList<string> MenuLabels(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[role='menu'] [role='menuitem']").Select(i => i.TextContent.Trim()).ToList();

    // ── The bar ──────────────────────────────────────────────────────────────

    [Fact]
    public void Standalone_RendersAToolbarButtonAndASiblingHiddenFileInput()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p
            .Add(c => c.Text, "Import JSON")
            .Add(c => c.Icon, "upload")
            .Add(c => c.Accept, ".json,.wireframe.json")
            .Add(c => c.Multiple, true));

        var button = cut.Find("button.tm-toolbar-btn");
        button.GetAttribute("type").Should().Be("button", "a native button - the picker opens through the hidden input, never through a form");
        button.TextContent.Trim().Should().Be("Import JSON");
        cut.FindAll(".tm-icon").Should().NotBeEmpty();

        var input = cut.Find("input[type='file']");
        input.GetAttribute("accept").Should().Be(".json,.wireframe.json");
        input.HasAttribute("multiple").Should().BeTrue();
        input.ClassList.Should().Contain("tm-toolbar-file-input");
        input.GetAttribute("tabindex").Should().Be("-1", "the visible button is the one tab stop");
        input.GetAttribute("aria-hidden").Should().Be("true");
        button.Contains(input).Should().BeFalse("the input is a sibling of the button, not inside it");
    }

    [Fact]
    public void ButtonTargetsTheInputById_ForTheNativeClickPath()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import"));

        var input = cut.Find("input[type='file']");
        input.Id.Should().NotBeNullOrWhiteSpace();
        cut.Find("button.tm-toolbar-btn").GetAttribute("data-tm-file-trigger").Should().Be(input.Id);
    }

    [Fact]
    public void NoKeyboardEmulation_TheNativeButtonActivatesItself()
    {
        // keyboard-activation-convention: a native button is never given Enter / Space handlers - the browser already
        // turns them into the click that opens the picker.
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import"));

        var button = cut.Find("button.tm-toolbar-btn");
        button.Attributes.Select(a => a.Name).Where(n => n.Contains("keydown") || n.Contains("keyup") || n.Contains("keypress"))
            .Should().BeEmpty();
        button.HasAttribute("tabindex").Should().BeFalse();
        button.HasAttribute("role").Should().BeFalse();
    }

    [Fact]
    public void Tooltip_BecomesTheTitleAndTheAccessibleNameOfAnIconButton()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p
            .Add(c => c.Icon, "upload")
            .Add(c => c.Tooltip, "Import a JSON file"));

        var button = cut.Find("button.tm-toolbar-btn");
        button.GetAttribute("title").Should().Be("Import a JSON file");
        button.GetAttribute("aria-label").Should().Be("Import a JSON file");
        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty();
    }

    [Fact]
    public void Disabled_DisablesTheButtonAndTheInput()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import").Add(c => c.Disabled, true));

        cut.Find("button.tm-toolbar-btn").HasAttribute("disabled").Should().BeTrue();
        cut.Find("input[type='file']").HasAttribute("disabled").Should().BeTrue("a disabled input can never open a chooser, even programmatically");
    }

    [Fact]
    public void ClassAndAdditionalAttributes_LandOnTheButton()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p
            .Add(c => c.Text, "Import")
            .Add(c => c.Class, "my-import")
            .AddUnmatched("data-custom", "x")
            .AddUnmatched("aria-describedby", "hint"));

        var button = cut.Find("button.tm-toolbar-btn");
        button.ClassList.Should().Contain("my-import");
        button.GetAttribute("data-custom").Should().Be("x");
        button.GetAttribute("aria-describedby").Should().Be("hint");
        cut.Find("input[type='file']").HasAttribute("data-custom").Should().BeFalse("splatted attributes belong to the visible control");
    }

    [Fact]
    public void DataTestId_GoesOnTheButton_AndTheInputGetsTheInputSuffix()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import").Add(c => c.DataTestId, "wd-import"));

        cut.Find("button.tm-toolbar-btn").GetAttribute("data-testid").Should().Be("wd-import");
        cut.Find("input[type='file']").GetAttribute("data-testid").Should().Be("wd-import-input");
    }

    [Fact]
    public void TestIdPrefix_NamespacesTheDefaultTestIds()
    {
        SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import").Add(c => c.TestIdPrefix, "ed1"));

        cut.Find("button.tm-toolbar-btn").GetAttribute("data-testid").Should().Be("ed1-toolbar-file-button");
        cut.Find("input[type='file']").GetAttribute("data-testid").Should().Be("ed1-toolbar-file-button-input");
    }

    // ── Toolbar integration: priority, rank, collapse ─────────────────────────

    [Theory]
    [InlineData(ToolbarButtonPriority.Primary, "2", "auto")]
    [InlineData(ToolbarButtonPriority.Secondary, "1", "auto")]
    [InlineData(ToolbarButtonPriority.Pinned, "2", "never")]
    public void InAToolbar_ItRegistersLikeAToolbarButton(ToolbarButtonPriority priority, string rank, string pin)
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(FileButton(priority: priority));

        var button = cut.Find("button.tm-toolbar-btn");
        button.GetAttribute("data-rank").Should().Be(rank);
        button.GetAttribute("data-pin").Should().Be(pin);
        button.HasAttribute("data-tm-toolbar-item").Should().BeTrue();
    }

    [Fact]
    public void Collapsed_TheButtonIsInertAndHidden_ButTheInputIsNotAndIsNeverRemounted()
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            builder.AddContent(1, FileButton(priority: ToolbarButtonPriority.Secondary));
        });
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-btn").Should().HaveCount(2));
        var inputBefore = cut.FindComponent<InputFile>().Instance;
        var elementBefore = cut.Find("input[type='file']");

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1));

        var button = cut.FindAll("button.tm-toolbar-btn").Single(b => b.TextContent.Contains("Import"));
        button.ClassList.Should().Contain("tm-toolbar-item--collapsed");
        button.HasAttribute("inert").Should().BeTrue();
        button.GetAttribute("aria-hidden").Should().Be("true");

        var input = cut.Find("input[type='file']");
        input.ClassList.Should().NotContain("tm-toolbar-item--collapsed", "the input sits OUTSIDE the collapse wrapper");
        input.HasAttribute("inert").Should().BeFalse();
        input.Closest(".tm-toolbar-item--collapsed").Should().BeNull();
        cut.FindComponent<InputFile>().Instance.Should().BeSameAs(inputBefore, "collapsing must not recreate the InputFile");
        input.GetAttribute("id").Should().Be(elementBefore.GetAttribute("id"), "...nor its DOM node");

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(2));

        cut.FindComponent<InputFile>().Instance.Should().BeSameAs(inputBefore, "expanding must not recreate it either");
        cut.Find("input[type='file']").GetAttribute("id").Should().Be(elementBefore.GetAttribute("id"));
    }

    [Fact]
    public void OverflowOnly_IsOnlyInTheMenu_ButTheInputIsStillThere()
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(FileButton(priority: ToolbarButtonPriority.OverflowOnly));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        cut.FindAll("button.tm-toolbar-btn").Should().BeEmpty("OverflowOnly never paints a bar button");
        cut.FindAll("input[type='file']").Should().HaveCount(1, "the menu entry needs an input to click");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Import");
    }

    // ── The More menu ─────────────────────────────────────────────────────────

    [Fact]
    public void MoreMenu_OffersAnOrdinaryEntry_WithTextAndIcon()
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            builder.AddContent(1, FileButton(text: "Import JSON", priority: ToolbarButtonPriority.Secondary));
        });
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-btn").Should().HaveCount(2));
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1));

        OpenMenu(cut);

        var entry = cut.Find("[role='menu'] [role='menuitem']");
        entry.TextContent.Trim().Should().Be("Import JSON");
        entry.QuerySelector(".tm-icon").Should().NotBeNull();
        entry.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void MoreMenu_Entry_IsTheNativeClickTarget_OfTheSameInputTheBarButtonOwns()
    {
        // The picker is opened by a NATIVE delegated click listener (a Blazor round-trip in between would sever the user
        // activation WebKit needs). What C# owes the module is the pair (input id, menu entry id); the menu entry carries
        // that id as data-action-id, the bar button carries the input id as data-tm-file-trigger.
        SetupToolbarModule();
        var file = SetupFileModule();
        var cut = RenderToolbar(FileButton(priority: ToolbarButtonPriority.OverflowOnly));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        cut.WaitForAssertion(() => file.Invocations["acquire"].Should().ContainSingle());
        var inputId = cut.Find("input[type='file']").Id;

        var args = file.Invocations["acquire"].Single().Arguments;
        args.Should().HaveCount(2);
        args[0].Should().Be(inputId);

        OpenMenu(cut);
        var entry = cut.Find("[role='menu'] [role='menuitem']");
        entry.GetAttribute("data-action-id").Should().Be((string)args[1]!, "the module matches the menu entry by this id");

        entry.Click();   // closes the menu; the chooser itself opens in the browser (E2E), never from C#
        cut.WaitForAssertion(() => cut.FindAll("[role='menu']").Should().BeEmpty());
    }

    [Fact]
    public void BarButton_CarriesTheInputIdAsItsTrigger_WhenOnTheBar()
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(FileButton(priority: ToolbarButtonPriority.Primary));

        var inputId = cut.Find("input[type='file']").Id;
        cut.Find("button.tm-toolbar-btn").GetAttribute("data-tm-file-trigger").Should().Be(inputId);
    }

    [Fact]
    public void MoreMenu_DisabledButton_OffersADisabledEntry()
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(FileButton(priority: ToolbarButtonPriority.OverflowOnly, disabled: true));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        cut.Find("button.tm-toolbar-more").HasAttribute("disabled").Should().BeTrue("an overflow of nothing but disabled entries has nothing to invoke");
        cut.Find("input[type='file']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void MoreMenu_DisabledEntry_NextToAnEnabledOne_IsRenderedDisabled()
    {
        SetupToolbarModule();
        SetupFileModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("Other", ToolbarButtonPriority.OverflowOnly)));
            builder.AddContent(1, FileButton(priority: ToolbarButtonPriority.OverflowOnly, disabled: true));
        });
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);

        var entries = cut.FindAll("[role='menu'] [role='menuitem']");
        entries.Single(e => e.TextContent.Contains("Import")).HasAttribute("disabled").Should().BeTrue();
        entries.Single(e => e.TextContent.Contains("Other")).HasAttribute("disabled").Should().BeFalse();
    }
    // ── Choosing files ────────────────────────────────────────────────────────

    [Fact]
    public async Task OnChange_ReceivesTheChosenFile()
    {
        SetupFileModule();
        InputFileChangeEventArgs? received = null;
        var cut = Render<TmToolbarFileButton>(p => p
            .Add(c => c.Text, "Import")
            .Add(c => c.OnChange, EventCallback.Factory.Create<InputFileChangeEventArgs>(this, e => received = e)));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{\"v\":1}", "doc.json"));

        received.Should().NotBeNull();
        received!.FileCount.Should().Be(1);
        received.File.Name.Should().Be("doc.json");
        using var reader = new StreamReader(received.File.OpenReadStream());
        (await reader.ReadToEndAsync()).Should().Be("{\"v\":1}");
    }

    [Fact]
    public void OnChange_IsNotRaisedForAnEmptySelection_ACancelledPickerIsNotAnImport()
    {
        SetupFileModule();
        var calls = 0;
        var cut = Render<TmToolbarFileButton>(p => p
            .Add(c => c.Text, "Import")
            .Add(c => c.OnChange, EventCallback.Factory.Create<InputFileChangeEventArgs>(this, _ => calls++)));

        // bUnit cannot upload zero files; a cancelled chooser reaches InputFile.OnChange as an empty file list.
        cut.InvokeAsync(() => cut.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([])));

        calls.Should().Be(0);
    }

    // ── The module wiring ─────────────────────────────────────────────────────

    [Fact]
    public async Task FirstRender_AcquiresTheNativeClickPath_AndDisposeReleasesIt()
    {
        var file = SetupFileModule();
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import"));

        cut.WaitForAssertion(() => file.Invocations["acquire"].Should().ContainSingle());
        var inputId = cut.Find("input[type='file']").Id;
        file.Invocations["acquire"].Single().Arguments[0].Should().Be(inputId);

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        file.Invocations["release"].Should().ContainSingle();
        file.Invocations["release"].Single().Arguments[0].Should().Be(inputId);
    }

    [Fact]
    public void WithoutJs_TheButtonStillRendersAndNothingThrows()
    {
        // No module set up: the import fails softly (LazyJsModule), the markup stays.
        var cut = Render<TmToolbarFileButton>(p => p.Add(c => c.Text, "Import"));

        cut.Find("button.tm-toolbar-btn").TextContent.Trim().Should().Be("Import");
        cut.Find("input[type='file']").Should().NotBeNull();
    }
}
