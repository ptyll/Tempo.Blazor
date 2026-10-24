using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Demo.Api.Services;

/// <summary>
/// Fixed sample wireframe documents used by the server-side preview endpoints so E2E tests can
/// exercise <c>IWireframeSvgRenderer</c> over real HTTP with deterministic, named scenarios.
/// </summary>
internal static class SampleWireframes
{
    public static WireframeDocument ForScenario(string? scenario) => scenario switch
    {
        "empty"      => Empty(),
        "unknown"    => Unknown(),
        "connectors" => Connectors(),
        _            => MultiPage(),
    };

    private static WireframeDocument MultiPage()
    {
        var doc = new WireframeDocument { Title = "Sample" };
        doc.Pages.Clear();
        var home = PageWith("Home", 800, 600, ("TmButton", 40, 40), ("TmCard", 40, 120));
        // A primary button: its fill is the theme's primary colour, so a themed preview visibly differs from the default one.
        home.Elements[0].Props["variant"] = System.Text.Json.JsonSerializer.SerializeToElement("primary");
        doc.Pages.Add(home);
        doc.Pages.Add(PageWith("Details", 1024, 768, ("TmDataTable", 30, 30)));
        doc.Pages.Add(PageWith("Settings", 640, 480, ("TmTextInput", 20, 20)));
        return doc;
    }

    private static WireframeDocument Empty()
    {
        var doc = new WireframeDocument { Title = "Empty" };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Name = "Blank Screen", Width = 500, Height = 400 });
        return doc;
    }

    private static WireframeDocument Unknown()
    {
        var doc = new WireframeDocument { Title = "Unknown" };
        doc.Pages.Clear();
        var page = new WireframePage { Name = "Custom", Width = 400, Height = 300 };
        page.Elements.Add(new WireframeElement { Type = "GhostWidget", X = 20, Y = 20, W = 160, H = 80 });
        doc.Pages.Add(page);
        return doc;
    }

    private static WireframeDocument Connectors()
    {
        var doc = new WireframeDocument { Title = "Flow" };
        doc.Pages.Clear();
        var page = new WireframePage { Name = "Flow", Width = 800, Height = 600 };
        page.Elements.Add(new WireframeElement { Id = "n1", Type = "TmButton", X = 40, Y = 40, W = 120, H = 40 });
        page.Elements.Add(new WireframeElement { Id = "n2", Type = "TmButton", X = 400, Y = 300, W = 120, H = 40 });
        page.Connectors.Add(new WireframeConnector { Id = "e1", FromId = "n1", ToId = "n2", Label = "next", EndArrow = "classic" });
        doc.Pages.Add(page);
        return doc;
    }

    private static WireframePage PageWith(string name, double w, double h, params (string Type, double X, double Y)[] elements)
    {
        var page = new WireframePage { Name = name, Width = w, Height = h };
        foreach (var (type, x, y) in elements)
            page.Elements.Add(new WireframeElement { Type = type, X = x, Y = y, W = 120, H = 40 });
        return page;
    }
}
