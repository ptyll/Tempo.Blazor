using Tempo.Blazor.Demo.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// The headless export lays the document out from the advance tables of the faces this catalog embeds, and the browser
/// lays it out with whatever "Aptos, Arial, sans-serif" resolves to on the machine. They can only agree on pagination
/// when the catalog's "Arial" is the face the OS resolves "Arial" to. On Linux that is Liberation Sans (the
/// metric-compatible Arial substitute), NOT DejaVu Sans: DejaVu is ~8 % wider with taller lines, which pushed the
/// contract demo from 4 browser pages to 3 server pages (Phase 10 export-parity red).
/// </summary>
public sealed class DemoDocumentExportFontCatalogTests
{
    private readonly ITestOutputHelper _output;

    public DemoDocumentExportFontCatalogTests(ITestOutputHelper output) => _output = output;

    private const string WindowsArial = @"C:\Windows\Fonts\arial.ttf";
    private const string LiberationRegular = "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf";
    private const string LiberationBold = "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf";

    [Fact]
    public void Arial_OnLinux_IsTheMetricCompatibleLiberationSansFace_NotDejaVu()
    {
        if (File.Exists(WindowsArial) || !File.Exists(LiberationRegular))
        {
            _output.WriteLine("Nothing checked: Windows (real Arial) or a Linux box without Liberation Sans at the Debian path.");
            return;
        }

        var catalog = new DemoDocumentExportFontCatalog();

        var regular = catalog.Fonts.First(face => face.Family == "Arial" && face.Weight == 400 && face.Style == "normal");
        Assert.Equal(File.ReadAllBytes(LiberationRegular), regular.Bytes);
        if (File.Exists(LiberationBold))
        {
            var bold = catalog.Fonts.First(face => face.Family == "Arial" && face.Weight == 700 && face.Style == "normal");
            Assert.Equal(File.ReadAllBytes(LiberationBold), bold.Bytes);
        }
    }

    [Fact]
    public void Aptos_IsAliasedToTheSameFaceAsArial()
    {
        var catalog = new DemoDocumentExportFontCatalog();
        if (!catalog.HasFonts)
        {
            _output.WriteLine("Nothing checked: the host has no known system font family.");
            return;
        }

        var arial = catalog.Fonts.First(face => face.Family == "Arial" && face.Weight == 400 && face.Style == "normal");
        var aptos = catalog.Fonts.First(face => face.Family == "Aptos" && face.Weight == 400 && face.Style == "normal");
        Assert.Equal(arial.Bytes, aptos.Bytes);
    }

    [Fact]
    public void AllFaces_ComeFromOneFamily_NeverMixed()
    {
        var catalog = new DemoDocumentExportFontCatalog();
        if (!catalog.HasFonts)
        {
            _output.WriteLine("Nothing checked: the host has no known system font family.");
            return;
        }

        _output.WriteLine($"Family: {catalog.FamilyName}");
        Assert.NotNull(catalog.FamilyName);
        // Every Arial slot must be a face of the ONE chosen family: its regular and bold faces must differ only by weight, so a bold from a
        // different family (different advances) can never be paired with the regular face.
        var arial = catalog.Fonts.Where(face => face.Family == "Arial").ToList();
        Assert.Contains(arial, face => face.Weight == 400 && face.Style == "normal");
        Assert.Equal(arial.Count, arial.Select(face => (face.Weight, face.Style)).Distinct().Count());
        if (catalog.FamilyName!.StartsWith("Liberation", StringComparison.Ordinal))
        {
            // DejaVu faces are ~8 % wider: none of its bytes may be present when Liberation was chosen.
            var dejavu = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
            if (File.Exists(dejavu))
            {
                var dejavuBytes = File.ReadAllBytes(dejavu);
                Assert.DoesNotContain(arial, face => face.Bytes.SequenceEqual(dejavuBytes));
            }
        }
    }
}
