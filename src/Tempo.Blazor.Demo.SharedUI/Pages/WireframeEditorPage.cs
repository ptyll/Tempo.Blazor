// Partial class for WireframeEditorPage – holds string constants that contain
// angle-bracket characters, which confuse the Razor parser when placed in @code blocks.

namespace Tempo.Blazor.Demo.SharedUI.Pages;

public partial class WireframeEditorPage
{
    private const string _schemaHint =
        "{\n" +
        "  \"version\": \"1.0\",\n" +
        "  \"title\": \"My wireframe\",\n" +
        "  \"width\": 1280,\n" +
        "  \"height\": 800,\n" +
        "  \"elements\": [\n" +
        "    {\n" +
        "      \"id\": \"abc12345\",\n" +
        "      \"type\": \"TmButton\",\n" +
        "      \"x\": 100, \"y\": 200,\n" +
        "      \"w\": 120, \"h\": 36,\n" +
        "      \"zIndex\": 0,\n" +
        "      \"props\": {\n" +
        "        \"label\": \"Click me\",\n" +
        "        \"variant\": \"primary\"\n" +
        "      }\n" +
        "    }\n" +
        "  ],\n" +
        "  \"connectors\": []\n" +
        "}";

    private const string _providerCode =
        "// 1. Implement IWireframeComponentProvider (MarketingComponentProvider.cs)\n" +
        "using System.Globalization;\n" +
        "\n" +
        "public class MarketingComponentProvider : IWireframeComponentProvider\n" +
        "{\n" +
        "    public string ProviderId => \"Marketing\";\n" +
        "    public int Priority => 10;   // higher than BuiltIn (0)\n" +
        "\n" +
        "    public IEnumerable<WireframeComponentDef> GetDefinitions()\n" +
        "    {\n" +
        "        yield return new WireframeComponentDef\n" +
        "        {\n" +
        "            Type         = \"HeroSection\",\n" +
        "            DisplayName  = \"Hero Section\",\n" +
        "            Category     = \"Marketing\",\n" +
        "            DefaultWidth = 820,\n" +
        "            DefaultHeight = 140,\n" +
        "            IsBuiltIn    = false,\n" +
        "            RenderSvg    = RenderHero,\n" +
        "            Props        = [ new PropDef { Name=\"heading\",    DisplayName=\"Heading\",    Type=PropType.String },\n" +
        "                             new PropDef { Name=\"subheading\", DisplayName=\"Subheading\", Type=PropType.String },\n" +
        "                             new PropDef { Name=\"showCta\",    DisplayName=\"Show CTA\",   Type=PropType.Bool   } ]\n" +
        "        };\n" +
        "        // ... FeatureCard, CtaBanner, FooterBar ...\n" +
        "    }\n" +
        "\n" +
        "    private static void RenderHero(WireframeElement el, RenderTreeBuilder b)\n" +
        "    {\n" +
        "        // Leaf markup: ONE frame holding complete, well-formed fragments —\n" +
        "        // Blazor parses each AddMarkupContent chunk into its OWN DOM template,\n" +
        "        // so an opening tag and its closing tag must never be split across frames.\n" +
        "        // User text is HTML-encoded; doubles are formatted invariantly.\n" +
        "        var heading    = System.Net.WebUtility.HtmlEncode(el.GetString(\"heading\", \"Hero\"));\n" +
        "        var subheading = System.Net.WebUtility.HtmlEncode(el.GetString(\"subheading\", \"\"));\n" +
        "        b.AddMarkupContent(0,\n" +
        "            string.Create(CultureInfo.InvariantCulture, $\"<rect width='{el.W}' height='{el.H}' rx='8' fill='#f0f9ff' stroke='#bae6fd'></rect>\") +\n" +
        "            $\"<text x='16' y='32' font-size='18' font-weight='600' fill='#374151'>{heading}</text>\" +\n" +
        "            $\"<text x='16' y='56' font-size='11' fill='#6b7280'>{subheading}</text>\");\n" +
        "\n" +
        "        // Container: a REAL element — children stay inside the group.\n" +
        "        b.OpenElement(1, \"g\");\n" +
        "        b.AddAttribute(2, \"transform\", string.Create(CultureInfo.InvariantCulture, $\"translate(16,{el.H - 40})\"));\n" +
        "        b.AddMarkupContent(3, \"<rect width='120' height='28' rx='6' fill='#dbeafe'></rect>\");\n" +
        "        b.CloseElement();\n" +
        "    }\n" +
        "}\n" +
        "\n" +
        "// 2. Register in DI (Program.cs / Startup.cs)\n" +
        "builder.Services.AddSingleton<IWireframeComponentProvider,\n" +
        "                              MarketingComponentProvider>();";
}
