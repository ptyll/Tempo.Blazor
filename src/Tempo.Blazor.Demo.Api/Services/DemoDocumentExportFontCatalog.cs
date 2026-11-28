using Tempo.Reporting.Engine.Pdf;

namespace Tempo.Blazor.Demo.Api.Services;

/// <summary>
/// Font faces for the demo headless document export. Loads the host system's sans-serif faces
/// (Arial on Windows, the metric-compatible Liberation Sans on Linux, DejaVu Sans as a last resort - ONE family as a whole, never mixed) once at startup and registers every face under both
/// the "Arial" and "Aptos" family names — demo documents use the
/// <c>Aptos, Arial, sans-serif</c> theme, and the alias keeps headless face resolution (and with
/// it measurement↔drawing parity) deterministic. <see cref="HasFonts"/> is false on hosts
/// without any known system face; snapshot-less exports then fail closed with
/// <c>TempoDocumentLayoutException</c> instead of degrading to non-WYSIWYG output.
/// </summary>
public sealed class DemoDocumentExportFontCatalog
{
    /// <summary>
    /// Candidate families in preference order. A family is taken as a WHOLE: the first one whose regular face exists supplies every slot
    /// (a missing bold / italic face stays missing rather than being filled from a different family, whose advances differ).
    /// </summary>
    private static readonly (string Name, (string Path, int Weight, string Style)[] Faces)[] Families =
    [
        ("Arial (Windows)",
        [
            (@"C:\Windows\Fonts\arial.ttf", 400, "normal"),
            (@"C:\Windows\Fonts\arialbd.ttf", 700, "normal"),
            (@"C:\Windows\Fonts\ariali.ttf", 400, "italic"),
            (@"C:\Windows\Fonts\arialbi.ttf", 700, "italic"),
        ]),
        // Linux: Liberation Sans is the metric-compatible Arial substitute fontconfig resolves "Arial" to, so the headless layout
        // measures the same advances (and line heights) the browser lays the live document out with. The directory differs per
        // distribution (Debian/Ubuntu, Fedora, Arch). DejaVu Sans stays the last resort - it is ~8 % wider, which changes pagination.
        ("Liberation Sans (Debian)", LiberationSans("/usr/share/fonts/truetype/liberation/")),
        ("Liberation Sans (Fedora)", LiberationSans("/usr/share/fonts/liberation-sans/")),
        ("Liberation Sans (Fedora, legacy)", LiberationSans("/usr/share/fonts/liberation/")),
        ("Liberation Sans (Fedora 39+)", LiberationSans("/usr/share/fonts/liberation-sans-fonts/")),
        ("Liberation Sans (Arch, TTF)", LiberationSans("/usr/share/fonts/TTF/")),
        ("DejaVu Sans",
        [
            ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 400, "normal"),
            ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 700, "normal"),
            ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf", 400, "italic"),
            ("/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf", 700, "italic"),
        ]),
    ];

    private static (string Path, int Weight, string Style)[] LiberationSans(string directory) =>
    [
        (directory + "LiberationSans-Regular.ttf", 400, "normal"),
        (directory + "LiberationSans-Bold.ttf", 700, "normal"),
        (directory + "LiberationSans-Italic.ttf", 400, "italic"),
        (directory + "LiberationSans-BoldItalic.ttf", 700, "italic"),
    ];

    /// <summary>Loads the faces of the first installed family once.</summary>
    public DemoDocumentExportFontCatalog()
    {
        var fonts = new List<ReportPdfFontFace>();
        foreach (var (name, faces) in Families)
        {
            if (!File.Exists(faces[0].Path))
            {
                continue; // the regular face decides whether the family is installed
            }

            foreach (var (path, weight, style) in faces)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(path);
                fonts.Add(new ReportPdfFontFace("Arial", weight, style, bytes));
                fonts.Add(new ReportPdfFontFace("Aptos", weight, style, bytes));
            }

            FamilyName = name;
            break;
        }

        Fonts = fonts;
    }

    /// <summary>The family the faces came from (null when no known family is installed).</summary>
    public string? FamilyName { get; }

    /// <summary>Font faces available for headless layout and PDF embedding.</summary>
    public IReadOnlyList<ReportPdfFontFace> Fonts { get; }

    /// <summary>True when at least one system face was found.</summary>
    public bool HasFonts => Fonts.Count > 0;
}
