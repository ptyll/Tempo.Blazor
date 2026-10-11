using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Tests.Components.Icons;

/// <summary>
/// F8 review L6: the F8 icon batch must be the lucide geometry, not a look-alike. Each icon's inner
/// markup is pinned to the lucide-static 1.55 elements (ISC), copied from the reference SVGs.
/// </summary>
public class LucideIconGeometryTests
{
    // Element markup copied verbatim from the lucide-static 1.55 reference (ISC). Attribute order is
    // normalised away; the geometry is what the assertion pins.
    public static readonly (string Name, string Elements)[] F8Icons =
    [
        ("tablet", "<rect width='16' height='20' x='4' y='2' rx='2' ry='2'/><line x1='12' x2='12.01' y1='18' y2='18'/>"),
        ("panel-left", "<rect width='18' height='18' x='3' y='3' rx='2'/><path d='M9 3v18'/>"),
        ("panel-left-close", "<rect width='18' height='18' x='3' y='3' rx='2'/><path d='M9 3v18'/><path d='m16 15-3-3 3-3'/>"),
        ("panel-right", "<rect width='18' height='18' x='3' y='3' rx='2'/><path d='M15 3v18'/>"),
        ("group", "<path d='M3 7V5c0-1.1.9-2 2-2h2'/><path d='M17 3h2c1.1 0 2 .9 2 2v2'/><path d='M21 17v2c0 1.1-.9 2-2 2h-2'/><path d='M7 21H5c-1.1 0-2-.9-2-2v-2'/><rect width='7' height='5' x='7' y='7' rx='1'/><rect width='7' height='5' x='10' y='12' rx='1'/>"),
        ("ungroup", "<rect x='11' y='14' width='10' height='7' rx='2'/><rect x='3' y='3' width='10' height='7' rx='2'/>"),
        ("magnet", "<path d='m12 15 4 4'/><path d='M2.352 10.648a1.205 1.205 0 0 0 0 1.704l2.296 2.296a1.205 1.205 0 0 0 1.704 0l6.029-6.029a1 1 0 1 1 3 3l-6.029 6.029a1.205 1.205 0 0 0 0 1.704l2.296 2.296a1.205 1.205 0 0 0 1.704 0l6.365-6.367A1 1 0 0 0 8.716 4.282z'/><path d='m5 8 4 4'/>"),
        ("spline", "<circle cx='19' cy='5' r='2'/><circle cx='5' cy='19' r='2'/><path d='M5 17A12 12 0 0 1 17 5'/>"),
        ("bring-to-front", "<rect x='8' y='8' width='8' height='8' rx='2'/><path d='M4 10a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2'/><path d='M14 20a2 2 0 0 0 2 2h4a2 2 0 0 0 2-2v-4a2 2 0 0 0-2-2'/>"),
        ("send-to-back", "<rect x='14' y='14' width='8' height='8' rx='2'/><rect x='2' y='2' width='8' height='8' rx='2'/><path d='M7 14v1a2 2 0 0 0 2 2h1'/><path d='M14 7h1a2 2 0 0 1 2 2v1'/>"),
        ("align-horizontal-distribute-center", "<rect width='6' height='14' x='4' y='5' rx='2'/><rect width='6' height='10' x='14' y='7' rx='2'/><path d='M17 22v-5'/><path d='M17 7V2'/><path d='M7 22v-3'/><path d='M7 5V2'/>"),
        ("align-vertical-distribute-center", "<path d='M22 17h-3'/><path d='M22 7h-5'/><path d='M5 17H2'/><path d='M7 7H2'/><rect x='5' y='14' width='14' height='6' rx='2'/><rect x='7' y='4' width='10' height='6' rx='2'/>"),
        ("align-vertical-justify-center", "<rect width='14' height='6' x='5' y='16' rx='2'/><rect width='10' height='6' x='7' y='2' rx='2'/><path d='M2 12h20'/>"),
        ("align-vertical-space-between", "<rect width='14' height='6' x='5' y='15' rx='2'/><rect width='10' height='6' x='7' y='3' rx='2'/><path d='M2 21h20'/><path d='M2 3h20'/>"),
        ("align-vertical-space-around", "<rect width='10' height='6' x='7' y='9' rx='2'/><path d='M22 20H2'/><path d='M22 4H2'/>"),
        ("scan", "<path d='M3 7V5a2 2 0 0 1 2-2h2'/><path d='M17 3h2a2 2 0 0 1 2 2v2'/><path d='M21 17v2a2 2 0 0 1-2 2h-2'/><path d='M7 21H5a2 2 0 0 1-2-2v-2'/>"),
        ("corner-up-left", "<path d='M20 20v-7a4 4 0 0 0-4-4H4'/><path d='M9 14 4 9l5-5'/>"),
    ];

    public static IEnumerable<object[]> Icons => F8Icons.Select(icon => new object[] { icon.Name, icon.Elements });

    [Theory]
    [MemberData(nameof(Icons))]
    public void F8Icon_MatchesTheLucideReference(string name, string reference)
    {
        MarkupElements(TmIcon.GetBuiltInSvg(name)).Should().Equal(MarkupElements(reference),
            "{0} must be the lucide geometry, not a look-alike", name);
    }

    private static IReadOnlyList<string> MarkupElements(string markup) =>
        XDocument.Parse($"<svg>{markup}</svg>").Root!.Elements().Select(Normalise).ToList();

    // Attribute order differs between the reference and the switch; compare the element name plus its
    // attributes sorted by name, with numbers canonicalised.
    private static string Normalise(XElement element)
    {
        var attributes = element.Attributes()
            .Select(attribute => $"{attribute.Name.LocalName}={Canonical(attribute.Value)}")
            .OrderBy(pair => pair, StringComparer.Ordinal);
        return element.Name.LocalName + " " + string.Join(" ", attributes);
    }

    private static string Canonical(string value) =>
        Regex.Replace(value, @"-?\d+(\.\d+)?", match =>
            double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture)
                .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
}
