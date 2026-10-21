using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Tests the pure logic helpers on TmWireframePropertiesPanel.
/// We call the internal static methods directly – no Blazor host or JS interop needed.
/// </summary>
public class WireframePropertiesPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WireframeElement MakeEl(string type = "TmButton",
        double x = 0, double y = 0, double w = 120, double h = 36)
        => new() { Type = type, X = x, Y = y, W = w, H = h };

    private static WireframeElement WithProp(WireframeElement el, string key, object val)
    {
        el.Props[key] = JsonSerializer.SerializeToElement(val);
        return el;
    }

    private static PropDef Prop(string name, PropType type = PropType.String, object? def = null,
        string[]? options = null)
        => new() { Name = name, DisplayName = name, Type = type, Default = def, Options = options };

    // ── MixedMarker ───────────────────────────────────────────────────────────

    [Fact]
    public void MixedMarker_IsDistinctFromCommonValues()
    {
        TmWireframePropertiesPanel.MixedMarker.Should().NotBeNullOrEmpty();
        TmWireframePropertiesPanel.MixedMarker.Should().NotBe("0");
        TmWireframePropertiesPanel.MixedMarker.Should().NotBe("true");
        TmWireframePropertiesPanel.MixedMarker.Should().NotBe("");
    }

    // ── IsMixedValue (static) ─────────────────────────────────────────────────

    [Fact]
    public void IsMixedValue_FalseForSingleElement()
    {
        var el = WithProp(MakeEl(), "label", "X");
        TmWireframePropertiesPanel.IsMixedValue([el], "label").Should().BeFalse();
    }

    [Fact]
    public void IsMixedValue_FalseWhenAllElementsHaveSameValue()
    {
        var el1 = WithProp(MakeEl(), "label", "Same");
        var el2 = WithProp(MakeEl(), "label", "Same");
        TmWireframePropertiesPanel.IsMixedValue([el1, el2], "label").Should().BeFalse();
    }

    [Fact]
    public void IsMixedValue_TrueWhenValuesDiffer()
    {
        var el1 = WithProp(MakeEl(), "label", "A");
        var el2 = WithProp(MakeEl(), "label", "B");
        TmWireframePropertiesPanel.IsMixedValue([el1, el2], "label").Should().BeTrue();
    }

    [Fact]
    public void IsMixedValue_TrueWhenOneElementMissingProp()
    {
        var el1 = WithProp(MakeEl(), "label", "A");
        var el2 = MakeEl(); // no label prop
        TmWireframePropertiesPanel.IsMixedValue([el1, el2], "label").Should().BeTrue();
    }

    [Fact]
    public void IsMixedValue_FalseForEmptyList()
    {
        TmWireframePropertiesPanel.IsMixedValue([], "label").Should().BeFalse();
    }

    // ── GetDisplayValue (static) ──────────────────────────────────────────────

    [Fact]
    public void GetDisplayValue_ReturnsStringProp()
    {
        var el   = WithProp(MakeEl(), "label", "Click me");
        var prop = Prop("label");
        TmWireframePropertiesPanel.GetDisplayValue([el], "label", prop)
            .Should().Be("Click me");
    }

    [Fact]
    public void GetDisplayValue_ReturnsFallbackDefault_WhenPropAbsent()
    {
        var el   = MakeEl();
        var prop = Prop("label", def: "Default text");
        TmWireframePropertiesPanel.GetDisplayValue([el], "label", prop)
            .Should().Be("Default text");
    }

    [Fact]
    public void GetDisplayValue_ReturnsBoolTrue()
    {
        var el   = WithProp(MakeEl(), "disabled", true);
        var prop = Prop("disabled", PropType.Bool);
        TmWireframePropertiesPanel.GetDisplayValue([el], "disabled", prop)
            .Should().Be("true");
    }

    [Fact]
    public void GetDisplayValue_ReturnsBoolFalse()
    {
        var el   = WithProp(MakeEl(), "disabled", false);
        var prop = Prop("disabled", PropType.Bool);
        TmWireframePropertiesPanel.GetDisplayValue([el], "disabled", prop)
            .Should().Be("false");
    }

    [Fact]
    public void GetDisplayValue_ReturnsIntAsString()
    {
        var el   = WithProp(MakeEl(), "count", 42);
        var prop = Prop("count", PropType.Int);
        TmWireframePropertiesPanel.GetDisplayValue([el], "count", prop)
            .Should().Be("42");
    }

    [Fact]
    public void GetDisplayValue_JoinsStringArrayWithComma()
    {
        var el   = WithProp(MakeEl(), "items", new[] { "A", "B", "C" });
        var prop = Prop("items", PropType.StringList);
        TmWireframePropertiesPanel.GetDisplayValue([el], "items", prop)
            .Should().Be("A, B, C");
    }

    [Fact]
    public void GetDisplayValue_ReturnsEmptyStringForEmptyList()
    {
        TmWireframePropertiesPanel.GetDisplayValue([], "label", Prop("label"))
            .Should().Be("");
    }

    [Fact]
    public void GetDisplayValue_UsesFirstElementWhenMultiple()
    {
        var el1 = WithProp(MakeEl(), "label", "First");
        var el2 = WithProp(MakeEl(), "label", "Second");
        var prop = Prop("label");
        TmWireframePropertiesPanel.GetDisplayValue([el1, el2], "label", prop)
            .Should().Be("First");
    }

    // ── New property types (Phase 4 coverage) ─────────────────────────────────

    [Fact]
    public void GetDisplayValue_ReturnsEnumString()
    {
        var el   = WithProp(MakeEl(), "variant", "primary");
        var prop = Prop("variant", PropType.Enum, options: ["primary", "secondary"]);
        TmWireframePropertiesPanel.GetDisplayValue([el], "variant", prop)
            .Should().Be("primary");
    }

    [Fact]
    public void GetDisplayValue_ReturnsDoubleAsString()
    {
        var el   = WithProp(MakeEl(), "opacity", 0.75);
        var prop = Prop("opacity", PropType.Double);
        TmWireframePropertiesPanel.GetDisplayValue([el], "opacity", prop)
            .Should().Be("0.75");
    }

    [Fact]
    public void GetDisplayValue_ReturnsColorString()
    {
        var el   = WithProp(MakeEl(), "color", "#ff5722");
        var prop = Prop("color", PropType.Color);
        TmWireframePropertiesPanel.GetDisplayValue([el], "color", prop)
            .Should().Be("#ff5722");
    }

    [Fact]
    public void GetDisplayValue_ReturnsIconString()
    {
        var el   = WithProp(MakeEl(), "icon", "check-circle");
        var prop = Prop("icon", PropType.Icon);
        TmWireframePropertiesPanel.GetDisplayValue([el], "icon", prop)
            .Should().Be("check-circle");
    }

    [Fact]
    public void IsMixedValue_TrueForDifferentEnumValues()
    {
        var el1 = WithProp(MakeEl(), "variant", "primary");
        var el2 = WithProp(MakeEl(), "variant", "secondary");
        TmWireframePropertiesPanel.IsMixedValue([el1, el2], "variant").Should().BeTrue();
    }

    [Fact]
    public void IsMixedValue_TrueForDifferentDoubleValues()
    {
        var el1 = WithProp(MakeEl(), "opacity", 0.5);
        var el2 = WithProp(MakeEl(), "opacity", 0.75);
        TmWireframePropertiesPanel.IsMixedValue([el1, el2], "opacity").Should().BeTrue();
    }
}

/// <summary>
/// bUnit tests of the redesigned inspector (wireframe redesign plan, phase 4): canvas / page / grid sections on an
/// empty selection, element name and delete on a selection.
/// </summary>
public class WireframePropertiesPanelRenderTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type) => new()
    {
        Type = type, DisplayName = type, Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { }
    };

    private static WireframeDocument NewDoc(double w = 1440, double h = 900)
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.EnsureActivePage();
        doc.ActivePage!.Width = w;
        doc.ActivePage.Height = h;
        return doc;
    }

    private WireframeElement AddEl(WireframeDocument doc, string? name = null)
    {
        var el = WireframeDocumentExtensions.NewElement("TmButton", 10, 10, 100, 30);
        el.Name = name;
        doc.Elements.Add(el);
        return el;
    }

    private IRenderedComponent<TmWireframePropertiesPanel> RenderPanel(
        WireframeDocument doc,
        string[]? selected = null,
        WireframeCommandStack? stack = null,
        bool readOnly = false,
        Action<ComponentParameterCollectionBuilder<TmWireframePropertiesPanel>>? more = null)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);

        return Render<TmWireframePropertiesPanel>(p =>
        {
            p.Add(c => c.Document, doc)
             .Add(c => c.SelectedIds, selected ?? [])
             .Add(c => c.ReadOnly, readOnly);
            if (stack is not null) p.AddCascadingValue(stack);
            more?.Invoke(p);
        });
    }

    // ── Element: name ─────────────────────────────────────────────────────────

    [Fact]
    public void ElementName_EditsWireframeElementName_Undoable()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, [el.Id], stack);

        var input = cut.Find("[data-testid='wd-element-name']");
        input.ClassList.Should().Contain("tm-wd-props__input");
        input.GetAttribute("placeholder").Should().Be("Unnamed");
        input.GetAttribute("value").Should().BeNullOrEmpty();

        input.Change("  Login button ");

        el.Name.Should().Be("Login button", "the value is trimmed");
        stack.CanUndo.Should().BeTrue();
        stack.NextUndoName.Should().Be("Rename element");
        stack.Undo();
        el.Name.Should().BeNull();
        stack.Redo();
        el.Name.Should().Be("Login button");
    }

    [Fact]
    public void ElementName_ShowsCurrentName_AndUnchangedValuePushesNoCommand()
    {
        var doc = NewDoc();
        var el = AddEl(doc, "Hero");
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, [el.Id], stack);

        var input = cut.Find("[data-testid='wd-element-name']");
        input.GetAttribute("value").Should().Be("Hero");

        input.Change("Hero");
        input.Change("  Hero  ");

        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ElementName_Blank_ClearsTheName()
    {
        var doc = NewDoc();
        var el = AddEl(doc, "Hero");
        var cut = RenderPanel(doc, [el.Id], new WireframeCommandStack());

        cut.Find("[data-testid='wd-element-name']").Change("   ");

        el.Name.Should().BeNull();
    }

    [Fact]
    public void ElementName_WithoutCommandStack_SetsDirectly_AndRaisesDocumentChanged()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var changed = 0;
        var cut = RenderPanel(doc, [el.Id], more: p => p.Add(c => c.DocumentChanged,
            EventCallback.Factory.Create<WireframeDocument>(this, _ => changed++)));

        cut.Find("[data-testid='wd-element-name']").Change("Direct");

        el.Name.Should().Be("Direct");
        changed.Should().Be(1);
    }

    [Fact]
    public void ElementName_IsNotOfferedForMultiSelection()
    {
        var doc = NewDoc();
        var a = AddEl(doc); var b = AddEl(doc);
        var cut = RenderPanel(doc, [a.Id, b.Id]);

        cut.FindAll("[data-testid='wd-element-name']").Should().BeEmpty();
    }

    [Fact]
    public void ElementName_ReadOnly_IsDisabled()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var cut = RenderPanel(doc, [el.Id], readOnly: true);

        cut.Find("[data-testid='wd-element-name']").HasAttribute("disabled").Should().BeTrue();
    }

    // ── Element: delete ───────────────────────────────────────────────────────

    [Fact]
    public void ElementSection_HasDeleteButton()
    {
        var doc = NewDoc();
        var a = AddEl(doc); var b = AddEl(doc);

        var single = RenderPanel(doc, [a.Id]);
        var btn = single.Find("button[data-testid='wd-delete-element']");
        btn.ClassList.Should().Contain("tm-wd-props__delete");
        btn.GetAttribute("type").Should().Be("button");
        btn.TextContent.Should().Contain("Delete element");
        btn.QuerySelector("svg.tm-icon").Should().NotBeNull();
        btn.Closest("details.tm-wd-props__section").Should().NotBeNull();

        var multi = Render<TmWireframePropertiesPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedIds, new[] { a.Id, b.Id }));
        multi.Find("button[data-testid='wd-delete-element']").TextContent.Should().Contain("Delete element");
    }

    [Fact]
    public void Delete_WithOnDeleteDelegate_InvokesItAndDoesNotRemoveDirectly()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var stack = new WireframeCommandStack();
        var invoked = 0;
        var cut = RenderPanel(doc, [el.Id], stack, more: p => p.Add(c => c.OnDelete,
            EventCallback.Factory.Create(this, () => invoked++)));

        cut.Find("[data-testid='wd-delete-element']").Click();

        invoked.Should().Be(1);
        doc.Elements.Should().ContainSingle();
        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Delete_WithoutDelegate_PushesRemoveCommand_Undoable()
    {
        var doc = NewDoc();
        var a = AddEl(doc); var b = AddEl(doc);
        var stack = new WireframeCommandStack();
        var changed = 0;
        var cut = RenderPanel(doc, [a.Id, b.Id], stack, more: p => p.Add(c => c.DocumentChanged,
            EventCallback.Factory.Create<WireframeDocument>(this, _ => changed++)));

        cut.Find("[data-testid='wd-delete-element']").Click();

        doc.Elements.Should().BeEmpty();
        changed.Should().Be(1);
        stack.Undo();
        doc.Elements.Should().HaveCount(2);
    }

    [Fact]
    public void Delete_WithoutStackAndDelegate_RemovesDirectly()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var cut = RenderPanel(doc, [el.Id]);

        cut.Find("[data-testid='wd-delete-element']").Click();

        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Delete_ReadOnly_IsDisabledAndInert()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var invoked = 0;
        var cut = RenderPanel(doc, [el.Id], readOnly: true, more: p => p.Add(c => c.OnDelete,
            EventCallback.Factory.Create(this, () => invoked++)));

        var btn = cut.Find("[data-testid='wd-delete-element']");
        btn.HasAttribute("disabled").Should().BeTrue();
        btn.Click();

        invoked.Should().Be(0);
        doc.Elements.Should().ContainSingle();
    }

    // ── Empty selection: canvas / page / grid ─────────────────────────────────

    [Fact]
    public void EmptySelection_ShowsCanvasPageGridSections_ThenTheSelectionHint()
    {
        var cut = RenderPanel(NewDoc());

        foreach (var id in new[] { "wd-section-canvas", "wd-section-page", "wd-section-grid" })
        {
            var section = cut.Find($"details[data-testid='{id}']");
            section.ClassList.Should().Contain("tm-wd-props__section");
            section.HasAttribute("open").Should().BeTrue();
        }
        cut.Find("details[data-testid='wd-section-canvas'] summary").TextContent.Should().Be("Canvas");
        cut.Find("details[data-testid='wd-section-page'] summary").TextContent.Should().Be("Page");
        cut.Find("details[data-testid='wd-section-grid'] summary").TextContent.Should().Be("Grid");

        cut.Find(".tm-wd-props__empty").TextContent.Should().Contain("Select an element on the canvas");
        cut.Find("details[data-testid='wd-section-selection'] summary").TextContent.Should().Be("Selection");
    }

    [Fact]
    public void WithASelection_CanvasPageGridSectionsAreNotRendered()
    {
        var doc = NewDoc();
        var el = AddEl(doc);
        var cut = RenderPanel(doc, [el.Id]);

        cut.FindAll("[data-testid='wd-section-canvas']").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-device-preset']").Should().BeEmpty();
    }

    // ── Device presets ────────────────────────────────────────────────────────

    [Fact]
    public void DevicePreset_ListsAllPresetsWithLocalizedNamesAndSizes()
    {
        var cut = RenderPanel(NewDoc());

        var select = cut.Find("select[data-testid='wd-device-preset']");
        select.ClassList.Should().Contain("tm-wd-props__select");
        select.GetAttribute("aria-label").Should().Be("Device preset");
        cut.FindAll("select[data-testid='wd-device-preset'] option").Select(o => o.TextContent).Should().Equal(
            "Desktop (1440×900)", "Laptop (1280×800)", "Tablet (1024×768)",
            "iPhone 14 (390×844)", "Android (360×800)", "Custom");
    }

    [Fact]
    public void DevicePreset_IPhone_Sets390x844_AndUndoRestores()
    {
        var doc = NewDoc(1440, 900);
        var stack = new WireframeCommandStack();
        (double W, double H)? resized = null;
        var changed = 0;
        var cut = RenderPanel(doc, stack: stack, more: p => p
            .Add(c => c.OnCanvasResized, EventCallback.Factory.Create<(double Width, double Height)>(this, s => resized = (s.Width, s.Height)))
            .Add(c => c.DocumentChanged, EventCallback.Factory.Create<WireframeDocument>(this, _ => changed++)));

        cut.Find("select[data-testid='wd-device-preset']").Change("iphone14");

        doc.ActivePage!.Width.Should().Be(390);
        doc.ActivePage.Height.Should().Be(844);
        resized.Should().Be((390d, 844d));
        changed.Should().Be(1);
        stack.NextUndoName.Should().Be("Resize canvas");
        cut.Find("[data-testid='wd-canvas-width']").GetAttribute("value").Should().Be("390");
        cut.Find("[data-testid='wd-canvas-height']").GetAttribute("value").Should().Be("844");

        stack.Undo();
        doc.ActivePage.Width.Should().Be(1440);
        doc.ActivePage.Height.Should().Be(900);
    }

    [Fact]
    public void DevicePreset_Custom_ChangesNothing()
    {
        var doc = NewDoc(1440, 900);
        var stack = new WireframeCommandStack();
        var resized = 0;
        var cut = RenderPanel(doc, stack: stack, more: p => p
            .Add(c => c.OnCanvasResized, EventCallback.Factory.Create<(double Width, double Height)>(this, _ => resized++)));

        cut.Find("select[data-testid='wd-device-preset']").Change("custom");

        doc.ActivePage!.Width.Should().Be(1440);
        doc.ActivePage.Height.Should().Be(900);
        stack.CanUndo.Should().BeFalse();
        resized.Should().Be(0);
        cut.Find("select[data-testid='wd-device-preset'] option[selected]").GetAttribute("value").Should().Be("custom");
    }

    [Theory]
    [InlineData(1440, 900, "desktop")]
    [InlineData(1280, 800, "laptop")]
    [InlineData(1024, 768, "tablet")]
    [InlineData(390, 844, "iphone14")]
    [InlineData(360, 800, "android")]
    [InlineData(500, 500, "custom")]
    public void DevicePreset_SelectedOption_FollowsTheActivePageSize(double w, double h, string expected)
    {
        var cut = RenderPanel(NewDoc(w, h));

        cut.Find("select[data-testid='wd-device-preset'] option[selected]").GetAttribute("value").Should().Be(expected);
    }

    [Fact]
    public void DevicePreset_ReadOnly_IsDisabled_AndChoosingDoesNothing()
    {
        var doc = NewDoc();
        var cut = RenderPanel(doc, readOnly: true);

        cut.Find("select[data-testid='wd-device-preset']").HasAttribute("disabled").Should().BeTrue();
    }

    // ── Canvas size inputs ────────────────────────────────────────────────────

    [Fact]
    public void CanvasSize_Inputs_AreLabelledNumberFieldsSeededFromThePage()
    {
        var cut = RenderPanel(NewDoc(1280, 800));

        var w = cut.Find("input[data-testid='wd-canvas-width']");
        var h = cut.Find("input[data-testid='wd-canvas-height']");
        w.GetAttribute("type").Should().Be("number");
        w.GetAttribute("min").Should().Be("100");
        w.GetAttribute("max").Should().Be("9999");
        w.GetAttribute("value").Should().Be("1280");
        h.GetAttribute("value").Should().Be("800");
        cut.Find($"label[for='{w.Id}']").TextContent.Should().Contain("Canvas width");
        cut.Find($"label[for='{h.Id}']").TextContent.Should().Contain("Canvas height");
        cut.Find("button[data-testid='wd-canvas-apply']").TextContent.Should().Contain("Apply");
    }

    [Fact]
    public void CanvasApply_PushesResizeCanvasCommand_AndRaisesOnCanvasResized()
    {
        var doc = NewDoc(1440, 900);
        var stack = new WireframeCommandStack();
        (double W, double H)? resized = null;
        var changed = 0;
        var cut = RenderPanel(doc, stack: stack, more: p => p
            .Add(c => c.OnCanvasResized, EventCallback.Factory.Create<(double Width, double Height)>(this, s => resized = (s.Width, s.Height)))
            .Add(c => c.DocumentChanged, EventCallback.Factory.Create<WireframeDocument>(this, _ => changed++)));

        cut.Find("[data-testid='wd-canvas-width']").Input("1000");
        cut.Find("[data-testid='wd-canvas-height']").Input("700");
        cut.Find("[data-testid='wd-canvas-apply']").Click();

        doc.ActivePage!.Width.Should().Be(1000);
        doc.ActivePage.Height.Should().Be(700);
        resized.Should().Be((1000d, 700d));
        changed.Should().Be(1);
        stack.NextUndoName.Should().Be("Resize canvas");
        stack.Undo();
        doc.ActivePage.Width.Should().Be(1440);
    }

    [Theory]
    [InlineData("99", "700")]
    [InlineData("1000", "50")]
    [InlineData("abc", "700")]
    [InlineData("1440", "900")]
    public void CanvasApply_InvalidTooSmallOrUnchanged_IsSkipped(string w, string h)
    {
        var doc = NewDoc(1440, 900);
        var stack = new WireframeCommandStack();
        var resized = 0;
        var cut = RenderPanel(doc, stack: stack, more: p => p
            .Add(c => c.OnCanvasResized, EventCallback.Factory.Create<(double Width, double Height)>(this, _ => resized++)));

        cut.Find("[data-testid='wd-canvas-width']").Input(w);
        cut.Find("[data-testid='wd-canvas-height']").Input(h);
        cut.Find("[data-testid='wd-canvas-apply']").Click();

        stack.CanUndo.Should().BeFalse();
        resized.Should().Be(0);
        doc.ActivePage!.Width.Should().Be(1440);
    }

    [Fact]
    public void CanvasSize_Inputs_ReseedWhenThePageSizeChangesExternally()
    {
        var doc = NewDoc(1440, 900);
        var cut = RenderPanel(doc);
        cut.Find("[data-testid='wd-canvas-width']").Input("1111");

        doc.ActivePage!.Width = 800;
        doc.ActivePage.Height = 600;
        cut.Render(p => p.Add(c => c.Document, doc));

        cut.Find("[data-testid='wd-canvas-width']").GetAttribute("value").Should().Be("800");
        cut.Find("[data-testid='wd-canvas-height']").GetAttribute("value").Should().Be("600");
    }

    [Fact]
    public void CanvasApply_WithoutCommandStack_ResizesDirectly()
    {
        var doc = NewDoc(1440, 900);
        var cut = RenderPanel(doc);

        cut.Find("[data-testid='wd-canvas-width']").Input("1000");
        cut.Find("[data-testid='wd-canvas-apply']").Click();

        doc.ActivePage!.Width.Should().Be(1000);
    }

    [Fact]
    public void ReadOnly_DisablesCanvasAndPageControls()
    {
        var doc = NewDoc(1440, 900);
        var cut = RenderPanel(doc, readOnly: true);

        foreach (var id in new[] { "wd-device-preset", "wd-canvas-width", "wd-canvas-height", "wd-canvas-apply", "wd-page-name" })
            cut.Find($"[data-testid='{id}']").HasAttribute("disabled").Should().BeTrue(id);

        cut.Find("[data-testid='wd-canvas-apply']").Click();
        doc.ActivePage!.Width.Should().Be(1440);
    }

    // ── Page name ─────────────────────────────────────────────────────────────

    [Fact]
    public void PageName_RenamesTheActivePageDirectly()
    {
        var doc = NewDoc();
        var changed = 0;
        var cut = RenderPanel(doc, more: p => p.Add(c => c.DocumentChanged,
            EventCallback.Factory.Create<WireframeDocument>(this, _ => changed++)));
        var input = cut.Find("input[data-testid='wd-page-name']");
        input.GetAttribute("value").Should().Be(doc.ActivePage!.Name);
        cut.Find($"label[for='{input.Id}']").TextContent.Should().Be("Name");

        input.Change("  Checkout ");

        doc.ActivePage.Name.Should().Be("Checkout");
        changed.Should().Be(1);
    }

    [Fact]
    public void PageName_Blank_KeepsTheOldName()
    {
        var doc = NewDoc();
        var old = doc.ActivePage!.Name;
        var cut = RenderPanel(doc);

        cut.Find("[data-testid='wd-page-name']").Change("   ");

        doc.ActivePage.Name.Should().Be(old);
    }

    // ── Grid ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Grid_Defaults_ShowGridOn_Size8()
    {
        var cut = RenderPanel(NewDoc());

        cut.Find("input[data-testid='wd-grid-show']").HasAttribute("checked").Should().BeTrue();
        cut.Find("input[data-testid='wd-grid-show']").Closest("label")!.ClassList.Should().Contain("tm-wd-props__toggle");
        var size = cut.Find("input[data-testid='wd-grid-size']");
        size.GetAttribute("value").Should().Be("8");
        size.GetAttribute("min").Should().Be("0");
        size.GetAttribute("max").Should().Be("128");
    }

    [Fact]
    public void Grid_Toggle_RaisesShowGridChanged_AndKeepsLocalState()
    {
        var raised = new List<bool>();
        var cut = RenderPanel(NewDoc(), more: p => p.Add(c => c.ShowGridChanged,
            EventCallback.Factory.Create<bool>(this, v => raised.Add(v))));

        cut.Find("[data-testid='wd-grid-show']").Change(false);

        raised.Should().Equal(false);
        cut.Find("[data-testid='wd-grid-show']").HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public void Grid_Size_RaisesGridSizeChanged_Clamped()
    {
        var raised = new List<int>();
        var cut = RenderPanel(NewDoc(), more: p => p.Add(c => c.GridSizeChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        cut.Find("[data-testid='wd-grid-size']").Change("16");
        cut.Find("[data-testid='wd-grid-size']").Change("0");
        cut.Find("[data-testid='wd-grid-size']").Change("500");
        cut.Find("[data-testid='wd-grid-size']").Change("-5");
        cut.Find("[data-testid='wd-grid-size']").Change("abc");

        raised.Should().Equal(16, 0, 128, 0);
    }

    [Fact]
    public void Grid_ReadoptsTheHostValue_WhenTheParameterChanges()
    {
        var cut = RenderPanel(NewDoc(), more: p => p
            .Add(c => c.ShowGrid, true).Add(c => c.GridSize, 8));
        cut.Find("[data-testid='wd-grid-size']").Change("24");   // local only: the host never fed it back

        cut.Render(p => p.Add(c => c.ShowGrid, false).Add(c => c.GridSize, 16));

        cut.Find("[data-testid='wd-grid-show']").HasAttribute("checked").Should().BeFalse();
        cut.Find("[data-testid='wd-grid-size']").GetAttribute("value").Should().Be("16");
    }

    [Fact]
    public void Grid_HostValueProvidedAtFirstRender_IsShown()
    {
        var cut = RenderPanel(NewDoc(), more: p => p
            .Add(c => c.ShowGrid, false).Add(c => c.GridSize, 0));

        cut.Find("[data-testid='wd-grid-show']").HasAttribute("checked").Should().BeFalse();
        cut.Find("[data-testid='wd-grid-size']").GetAttribute("value").Should().Be("0");
    }
}
