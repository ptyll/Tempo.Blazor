using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// The demo custom component provider behind the "Custom Component Provider" section of
/// <c>/wireframe-editor</c>: three marketing components (<c>HeroSection</c>, <c>FeatureCard</c>,
/// <c>CtaBanner</c>) registered alongside the built-in Tempo.Blazor set, so the toolbox shows the
/// All / Built-in / Custom filter tabs exactly like a host registration would. The element types
/// and prop keys match the <c>custom-components</c> sample document served by the Demo.Api.
/// </summary>
public sealed class DemoMarketingComponentProvider : IWireframeComponentProvider
{
    /// <inheritdoc/>
    public string ProviderId => "Demo.Marketing";

    /// <inheritdoc/>
    public int Priority => 10; // higher than BuiltIn (0)

    /// <inheritdoc/>
    public IEnumerable<WireframeComponentDef> GetDefinitions()
    {
        yield return new WireframeComponentDef
        {
            Type = "HeroSection",
            DisplayName = "Hero Section",
            Category = "Marketing",
            DefaultWidth = 820,
            DefaultHeight = 140,
            IsBuiltIn = false,
            RenderSvg = RenderHero,
            Props =
            [
                new PropDef { Name = "headline", DisplayName = "Headline", Type = PropType.String },
                new PropDef { Name = "subtext", DisplayName = "Subtext", Type = PropType.String },
            ],
        };
        yield return new WireframeComponentDef
        {
            Type = "FeatureCard",
            DisplayName = "Feature Card",
            Category = "Marketing",
            DefaultWidth = 340,
            DefaultHeight = 180,
            IsBuiltIn = false,
            RenderSvg = RenderFeatureCard,
            Props =
            [
                new PropDef { Name = "title", DisplayName = "Title", Type = PropType.String },
                new PropDef { Name = "icon", DisplayName = "Icon", Type = PropType.String },
            ],
        };
        yield return new WireframeComponentDef
        {
            Type = "CtaBanner",
            DisplayName = "CTA Banner",
            Category = "Marketing",
            DefaultWidth = 640,
            DefaultHeight = 120,
            IsBuiltIn = false,
            RenderSvg = RenderCtaBanner,
            Props =
            [
                new PropDef { Name = "text", DisplayName = "Text", Type = PropType.String },
                new PropDef { Name = "buttonLabel", DisplayName = "Button label", Type = PropType.String },
            ],
        };
    }

    // Markup-frame contract: every AddMarkupContent chunk is ONE complete, well-formed fragment
    // (sibling elements are fine; an open/close pair is never split across frames). User text is
    // HTML-encoded, doubles are formatted invariantly.
    private static void RenderHero(WireframeElement el, RenderTreeBuilder b)
    {
        var headline = WebUtility.HtmlEncode(el.Props.GetString("headline", "Hero headline"));
        var subtext = WebUtility.HtmlEncode(el.Props.GetString("subtext", ""));
        b.AddMarkupContent(0,
            string.Create(CultureInfo.InvariantCulture, $"<rect width='{el.W}' height='{el.H}' rx='8' fill='#f0f9ff' stroke='#bae6fd'></rect>")
            + string.Create(CultureInfo.InvariantCulture, $"<text x='24' y='44' font-size='22' font-weight='600' fill='#374151'>{headline}</text>")
            + string.Create(CultureInfo.InvariantCulture, $"<text x='24' y='72' font-size='13' fill='#6b7280'>{subtext}</text>"));
    }

    private static void RenderFeatureCard(WireframeElement el, RenderTreeBuilder b)
    {
        var title = WebUtility.HtmlEncode(el.Props.GetString("title", "Feature"));
        b.AddMarkupContent(0,
            string.Create(CultureInfo.InvariantCulture, $"<rect width='{el.W}' height='{el.H}' rx='8' fill='#ffffff' stroke='#e5e7eb'></rect>")
            + string.Create(CultureInfo.InvariantCulture, $"<circle cx='28' cy='28' r='14' fill='#dbeafe'></circle>")
            + string.Create(CultureInfo.InvariantCulture, $"<text x='52' y='34' font-size='14' font-weight='600' fill='#374151'>{title}</text>")
            + string.Create(CultureInfo.InvariantCulture, $"<rect x='16' y='56' width='{el.W - 32}' height='10' rx='5' fill='#f3f4f6'></rect>")
            + string.Create(CultureInfo.InvariantCulture, $"<rect x='16' y='74' width='{el.W - 72}' height='10' rx='5' fill='#f3f4f6'></rect>"));
    }

    private static void RenderCtaBanner(WireframeElement el, RenderTreeBuilder b)
    {
        var text = WebUtility.HtmlEncode(el.Props.GetString("text", "Call to action"));
        var buttonLabel = WebUtility.HtmlEncode(el.Props.GetString("buttonLabel", "Learn more"));
        b.AddMarkupContent(0,
            string.Create(CultureInfo.InvariantCulture, $"<rect width='{el.W}' height='{el.H}' rx='8' fill='#eef2ff' stroke='#c7d2fe'></rect>")
            + string.Create(CultureInfo.InvariantCulture, $"<text x='24' y='{el.H / 2 + 5}' font-size='16' font-weight='600' fill='#374151'>{text}</text>")
            + string.Create(CultureInfo.InvariantCulture, $"<g transform='translate({el.W - 168},{el.H / 2 - 16})'><rect width='144' height='32' rx='6' fill='#4f46e5'></rect><text x='72' y='21' font-size='12' font-weight='600' fill='#ffffff' text-anchor='middle'>{buttonLabel}</text></g>"));
    }
}
