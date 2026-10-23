using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TestIdPrefix</c> of <see cref="TmWireframeEditor"/> reaches every child that inherits <c>TmComponentBase</c>
/// (canvas, inspector, pages panel, side panel, sheets), so two editors on one page never share a <c>wd-</c> test id.
/// </summary>
public class WireframeTestIdPrefixTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type) => new()
    {
        Type = type, DisplayName = type, Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { }
    };

    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test", Width = 1200, Height = 800 };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Home", Width = 1200, Height = 800 });
        doc.Pages.Add(new WireframePage { Id = "p2", Name = "Pricing", Width = 800, Height = 600 });
        doc.ActivePageId = "p1";
        return doc;
    }

    private IRenderedComponent<TmWireframeEditor> RenderEditor(string? prefix, TmLayoutMode mode = TmLayoutMode.Desktop)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);
        return Render<TmWireframeEditor>(p =>
        {
            p.Add(c => c.Document, NewDocument());
            p.Add(c => c.LayoutMode, mode);
            p.Add(c => c.PageNavigation, WireframePageNavigation.Panel);
            if (prefix is not null) p.Add(c => c.TestIdPrefix, prefix);
        });
    }

    private static List<string> TestIds(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.FindAll("[data-testid]").Select(e => e.GetAttribute("data-testid")!).ToList();

    private static void ShowPagesTab(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.FindAll("[data-region='left'] [role='tab']").First(t => t.TextContent.Trim() == "Pages").Click();

    [Fact]
    public void Prefix_Desktop_LeavesNoUnprefixedWdTestId()
    {
        var cut = RenderEditor("narrow");
        ShowPagesTab(cut);

        var ids = TestIds(cut);
        ids.Should().Contain("narrow-wd-side-panel").And.Contain("narrow-wd-pages-panel").And.Contain("narrow-wd-section-canvas");
        ids.Where(i => i.Contains("wd-") && !i.StartsWith("narrow-", StringComparison.Ordinal))
            .Should().BeEmpty("every wd- test id carries the host's prefix");
    }

    [Fact]
    public void Prefix_Tablet_Sheets_CarryThePrefix()
    {
        var cut = RenderEditor("narrow", TmLayoutMode.Tablet);
        cut.Find("[data-testid='narrow-wd-toggle-left']").Click();

        var ids = TestIds(cut);
        ids.Should().Contain("narrow-wd-left-drawer");
        ids.Where(i => i.Contains("wd-") && !i.StartsWith("narrow-", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void NoPrefix_DefaultOutput_IsUnchanged()
    {
        var cut = RenderEditor(null);
        ShowPagesTab(cut);

        var ids = TestIds(cut);
        ids.Should().Contain("wd-side-panel").And.Contain("wd-pages-panel").And.Contain("wd-section-canvas").And.Contain("wd-toolbar");
        ids.Should().NotContain(i => i.StartsWith("-", StringComparison.Ordinal) || i.Contains("--"));
    }
}
