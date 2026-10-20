using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class DiagramToolboxAccessibilityPhase10Tests : LocalizationTestBase
{
    private const string LibraryJson = """
        {
          "setId": "phase10.diagram",
          "nameResourceKey": "DiagramLibrary_Phase10",
          "palettes": [
            {
              "paletteId": "phase10.diagram.core",
              "nameResourceKey": "DiagramPalette_Phase10Core",
              "order": 10,
              "stencils": [
                {
                  "id": "phase10.diagram.core.node",
                  "nameResourceKey": "DiagramStencil_Phase10Node",
                  "category": "phase10.diagram",
                  "origin": "tempoOriginal"
                }
              ]
            }
          ]
        }
        """;

    public DiagramToolboxAccessibilityPhase10Tests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDiagramToolbox_Toggle"] = "Toggle toolbox",
            ["TmDiagramToolbox_Title"] = "Toolbox",
            ["TmDiagramToolbox_SearchPlaceholder"] = "Search stencils",
            ["TmDiagramToolbox_SearchAriaLabel"] = "Search diagram stencils",
            ["TmDiagramToolbox_NoResults"] = "No matching stencils",
            ["TmDiagramToolbox_DragStencil"] = "Drag {0} onto the canvas",
            ["TmDiagramToolbox_InsertStencil"] = "Insert {0} onto the canvas",
            ["DiagramLibrary_Phase10"] = "Phase 10 Library",
            ["DiagramPalette_Phase10Core"] = "Core",
            ["DiagramStencil_Phase10Node"] = "Accessible Node"
        });

        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new JsonDiagramStencilProvider(
            [JsonDiagramStencilLibrarySource.Required("phase10", () => LibraryJson)]));
        Services.AddSingleton(registry);
    }

    [Fact]
    public void Search_Input_Has_Localized_Aria_Label()
    {
        var cut = Render<TmDiagramToolbox>();

        var search = cut.Find(".tm-diagram-toolbox__search input");

        search.GetAttribute("aria-label").Should().Be("Search diagram stencils");
        search.GetAttribute("placeholder").Should().Be("Search stencils");
    }

    [Fact]
    public void Stencil_Item_Has_Localized_Aria_Label_Without_Duplicating_Visible_Label()
    {
        var cut = Render<TmDiagramToolbox>();

        var item = cut.Find("[data-stencil-id='phase10.diagram.core.node']");
        var label = item.QuerySelector(".tm-diagram-toolbox__label");

        item.GetAttribute("aria-label").Should().Be("Insert Accessible Node onto the canvas");
        item.GetAttribute("title").Should().Be("Drag Accessible Node onto the canvas");
        label.Should().NotBeNull();
        label!.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Enter_InsertsOnKeyDown_ExactlyOnce_IgnoringRepeat()
    {
        var count = 0;
        var cut = Render<TmDiagramToolbox>(p => p.Add(c => c.StencilKeyboardInsert,
            EventCallback.Factory.Create<string>(this, _ => count++)));
        var tile = cut.Find("[data-stencil-id='phase10.diagram.core.node']");
        tile.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        tile.KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        tile.KeyUp(new KeyboardEventArgs { Key = "Enter" });
        count.Should().Be(1);
    }

    [Fact]
    public void Space_InsertsOnKeyUp_NotOnKeyDown()
    {
        var count = 0;
        var cut = Render<TmDiagramToolbox>(p => p.Add(c => c.StencilKeyboardInsert,
            EventCallback.Factory.Create<string>(this, _ => count++)));
        var tile = cut.Find("[data-stencil-id='phase10.diagram.core.node']");
        tile.KeyDown(new KeyboardEventArgs { Key = " " });
        count.Should().Be(0);
        tile.KeyUp(new KeyboardEventArgs { Key = " " });
        count.Should().Be(1);
    }

    [Fact]
    public void ReadOnly_TilesAreInert()
    {
        var count = 0;
        var cut = Render<TmDiagramToolbox>(p => p
            .Add(c => c.ReadOnly, true)
            .Add(c => c.StencilKeyboardInsert, EventCallback.Factory.Create<string>(this, _ => count++)));
        var tile = cut.Find("[data-stencil-id='phase10.diagram.core.node']");
        tile.GetAttribute("aria-disabled").Should().Be("true");
        tile.GetAttribute("draggable").Should().Be("false");
        tile.Click(); tile.KeyDown(new KeyboardEventArgs { Key = "Enter" }); tile.KeyUp(new KeyboardEventArgs { Key = " " });
        count.Should().Be(0);
    }

    [Fact]
    public void SearchInput_HasNoInvalidClearableAttribute()
    {
        var cut = Render<TmDiagramToolbox>();

        cut.Find(".tm-diagram-toolbox__search input").HasAttribute("clearable")
            .Should().BeFalse("TmSearchInput has no Clearable parameter — an unknown attribute would render verbatim");
    }

    [Fact]
    public void Toolbox_Css_Defines_Focus_Visible_Rings_For_Items_And_Palette_Headers()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css"));

        css.Should().Contain(".tm-diagram-toolbox__item:focus-visible");
        css.Should().Contain(".tm-diagram-toolbox__category-header:focus-visible");
        css.Should().Contain("outline");
        css.Should().Contain("--tm-color-primary");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
