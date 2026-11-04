using FluentAssertions.Execution;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

public class TempoPackFormControlsRenderTests
{
    private static readonly string[] FormControlCategories =
    [
        "Buttons",
        "Inputs",
        "Tags",
        "Pickers",
        "Dropdowns"
    ];

    private static readonly string[] FormControlTypes = new BuiltInComponentSchemas()
        .GetSchemas()
        .Where(schema => FormControlCategories.Contains(schema.Category, StringComparer.Ordinal))
        .Select(schema => schema.Type)
        .ToArray();

    public static TheoryData<string> FormControlTypeData
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in FormControlTypes)
                data.Add(type);
            return data;
        }
    }

    public static TheoryData<string, (string Key, object? Value)[], string[]> RenderCases
    {
        get
        {
            var data = new TheoryData<string, (string Key, object? Value)[], string[]>();
            Add(data, "TmSplitButton", [("label", "Actions")], "Actions");
            Add(data, "TmCopyButton", [], "rect");
            Add(data, "TmFloatingActionButton", [("icon", "plus")], "circle");
            Add(data, "TmTextInput", [("label", "Customer"), ("placeholder", "Enter name")], "Customer", "Enter name");
            Add(data, "TmTextArea", [("label", "Notes"), ("placeholder", "Long notes")], "Notes", "Long notes");
            Add(data, "TmNumberInput", [("label", "Quantity")], "Quantity");
            Add(data, "TmSearchInput", [("placeholder", "Search orders")], "Search orders");
            Add(data, "TmCurrencyInput", [("label", "Total"), ("currencySymbol", "$")], "Total", "$");
            Add(data, "TmCheckbox", [("label", "Accepted"), ("checked", true)], "Accepted");
            Add(data, "TmRadio", [("label", "Option A"), ("checked", true)], "Option A");
            Add(data, "TmRadioGroup", [("label", "Choice"), ("options", new[] { "One", "Two" })], "Choice", "One", "Two");
            Add(data, "TmSegmentedControl",
                [("label", "Period"), ("options", new[] { "Day", "Week" }), ("active", 0), ("sizeVariant", "Md"), ("badge", "New")],
                "Period", "Day", "Week", "New");
            Add(data, "TmToggle", [("label", "Enabled"), ("checked", true)], "Enabled");
            Add(data, "TmToggleSection", [("label", "Advanced"), ("expanded", true)], "Advanced");
            Add(data, "TmSelect", [("label", "Country"), ("placeholder", "Choose country")], "Country", "Choose country");
            Add(data, "TmMultiSelect", [("label", "Roles"), ("placeholder", "Pick roles")], "Roles", "Item");
            Add(data, "TmCascadingSelect", [("label", "Region"), ("levels", 3)], "Region");
            Add(data, "TmFilterableDropdown", [("label", "Status"), ("placeholder", "Filter status")], "Status", "Filter status");
            Add(data, "TmEntityPicker", [("label", "Owner"), ("placeholder", "Choose owner")], "Owner", "Choose owner");
            Add(data, "TmExpressionEditor", [("label", "Rule"), ("placeholder", "amount > 0")], "Rule", "amount &gt; 0");
            Add(data, "TmPasswordStrengthIndicator", [("strength", 4)], "Strong");
            Add(data, "TmSlider", [("label", "Progress"), ("value", 60)], "Progress", "60");
            Add(data, "TmRangeSlider", [("label", "Window"), ("from", 20), ("to", 80)], "Window", "20", "80");
            Add(data, "TmRating", [("value", 4), ("max", 5)], "#f59e0b");
            Add(data, "TmMaskedTextBox", [("label", "Birth"), ("mask", "__.__.____")], "Birth", "__.__.____");
            Add(data, "TmMultiColumnComboBox", [("label", "Account"), ("placeholder", "Select account")], "Account", "Select account");
            Add(data, "TmSignature", [("placeholder", "Sign here")], "Sign here");
            Add(data, "TmSignatureCapture", [("placeholder", "Draw signature")], "Draw signature", "Confirm");
            Add(data, "TmTagPicker", [("tags", new[] { "Alpha", "Beta" }), ("allowCreate", true)], "Alpha", "Beta", "Create new tag");
            Add(data, "TmDatePicker", [("label", "Due date"), ("format", "yyyy-mm-dd")], "Due date", "yyyy-mm-dd");
            Add(data, "TmDateTimePicker", [("label", "Start"), ("format", "yyyy-mm-dd HH:mm")], "Start", "yyyy-mm-dd HH:mm");
            Add(data, "TmTimePicker", [("label", "Time")], "Time", "HH:MM");
            Add(data, "TmDateRangePicker", [("label", "Period")], "Period", "From", "To");
            Add(data, "TmTimeRangePicker", [("label", "Hours")], "Hours", "HH:MM");
            Add(data, "TmDateTimeRangePicker", [("label", "Booking")], "Booking", "From date", "To date");
            Add(data, "TmTimeInput", [], "HH", "MM");
            Add(data, "TmCalendarView", [("month", "March"), ("year", 2026), ("selectedDay", 17)], "March 2026", "17");
            Add(data, "TmCalendarGrid", [("month", "April"), ("year", 2026)], "April 2026", "31");
            Add(data, "TmRecurrenceEditor", [("frequency", "weekly"), ("interval", 2)], "Repeat every", "2", "weekly");
            Add(data, "TmDropdown", [("text", "Options"), ("icon", "user")], "Options");
            Add(data, "TmDropdownItem", [("label", "Archive"), ("icon", "box")], "Archive");
            return data;
        }
    }

    [Fact]
    public void FormControlTypeList_CoversCompleteSchemaCategories()
    {
        FormControlTypes.Should().HaveCount(45);
        FormControlTypes.Should().Contain(["TmButton", "TmTextInput", "TmQueryInput", "TmDecimalInput", "TmDatePicker", "TmTagPicker", "TmDropdownItem"]);
    }

    [Fact]
    public async Task TmButton_Primary_RendersBoundLabelAndTokenColor()
    {
        var svg = await RenderAsync(
            "TmButton",
            ("label", "Ulozit"),
            ("variant", "primary"));

        svg.Should().Contain(">Ulozit<");
        svg.Should().Contain("#3b82f6");
        svg.Should().NotContainEquivalentOf("TmButton");
    }

    [Theory]
    [MemberData(nameof(RenderCases))]
    public async Task FormControls_RenderBoundContentAndExpectedAffordances(
        string type,
        (string Key, object? Value)[] props,
        string[] expectedFragments)
    {
        var svg = await RenderAsync(type, props);

        using var _ = new AssertionScope(type);
        foreach (var fragment in expectedFragments)
            svg.Should().Contain(fragment);
        svg.Should().NotContain("Missing component");
    }

    [Fact]
    public async Task TmButton_DisabledAndLoadingStates_AreDeclarative()
    {
        var disabledSvg = await RenderAsync(
            "TmButton",
            ("label", "Disabled"),
            ("disabled", true));
        var loadingSvg = await RenderAsync(
            "TmButton",
            ("label", "Save"),
            ("loading", true),
            ("loadingText", "Saving"));

        disabledSvg.Should().Contain("opacity='0.45'");
        loadingSvg.Should().Contain("opacity='0.7'");
        loadingSvg.Should().Contain("data-stencil-kind='spinner'");
        loadingSvg.Should().Contain(">Saving<");
    }

    [Fact]
    public async Task TmRadioGroup_OffsetsOptionTextPastSelectionCircle()
    {
        var svg = await RenderAsync(
            "TmRadioGroup",
            ("label", "Choice"),
            ("options", new[] { "One", "Two" }));

        svg.Should().Contain(">One<");
        svg.Should().Contain("transform='translate(0,24)'");
        svg.Should().Contain("x='28'");
    }

    [Fact]
    public async Task TmRangeSlider_OffsetsValueTextAwayFromLabel()
    {
        var svg = await RenderAsync(
            "TmRangeSlider",
            ("label", "Window"),
            ("from", 20),
            ("to", 80));

        svg.Should().Contain(">Window<");
        svg.Should().Contain(">20<");
        svg.Should().Contain("x='48'");
        svg.Should().Contain("x='132'");
    }

    [Fact]
    public async Task LeadingIconFields_OffsetPlaceholderPastIcon()
    {
        var dropdownSvg = await RenderAsync(
            "TmFilterableDropdown",
            ("label", "Status"),
            ("placeholder", "Filter status"));
        var pickerSvg = await RenderAsync(
            "TmEntityPicker",
            ("label", "Owner"),
            ("placeholder", "Choose owner"));

        dropdownSvg.Should().Contain(">Filter status<");
        dropdownSvg.Should().Contain("x='32'");
        pickerSvg.Should().Contain(">Choose owner<");
        pickerSvg.Should().Contain("x='32'");
    }

    [Theory]
    [MemberData(nameof(FormControlTypeData))]
    public void FormControlDefinitions_PreserveSchemaMetadata_AndComeFromTempoPack(string type)
    {
        var registry = Registry();
        var def = registry.GetDef(type);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);

        using var _ = new AssertionScope(type);
        def.Should().NotBeNull();
        def!.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull();
        def.Category.Should().Be(schema.Category);
        def.DisplayName.Should().Be(schema.DisplayName);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
        def.SizePresets.Should().BeEquivalentTo(schema.SizePresets);
    }

    private static void Add(
        TheoryData<string, (string Key, object? Value)[], string[]> data,
        string type,
        (string Key, object? Value)[] props,
        params string[] fragments)
        => data.Add(type, props, fragments);

    private static async Task<string> RenderAsync(
        string type,
        params (string Key, object? Value)[] props)
    {
        var def = Registry().GetDef(type) ?? throw new InvalidOperationException($"Missing definition for {type}.");
        var element = new WireframeElement
        {
            Id = "sut",
            Type = type,
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };
        foreach (var (key, value) in props)
            element.SetProp(key, value);

        return await RenderFragmentAsync(builder => def.RenderSvg(element, builder));
    }

    private static WireframeComponentRegistry Registry()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    private static async Task<string> RenderFragmentAsync(RenderFragment fragment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
