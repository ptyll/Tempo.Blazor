using System.Collections.Generic;
using System.Globalization;
using Tempo.Blazor.Localization;

namespace Tempo.ReportServer.Web.Tests.Fixtures;

/// <summary>
/// Dictionary-backed <see cref="ITmLocalizer"/> mirroring the main app's test mock. The portal already
/// resolves the REAL JSON-backed localizer through <c>AddTempoBlazorReporting()</c>, so most tests use
/// that (and <see cref="ReportServerWebTestBase.UseUiCulture"/> to prove CS/FR). This mock is available
/// for isolated tests that want to seed specific keys deterministically and override <c>Loc</c> via
/// <c>Services.AddSingleton&lt;ITmLocalizer&gt;(...)</c> (last registration wins in .NET DI).
/// </summary>
public sealed class MockTmLocalizer : ITmLocalizer
{
    private readonly IReadOnlyDictionary<string, string> _data;

    public MockTmLocalizer(IReadOnlyDictionary<string, string> data) => _data = data;

    public string this[string key] => _data.TryGetValue(key, out var value) ? value : $"[{key}]";

    public string this[string key, params object[] arguments]
    {
        get
        {
            var template = _data.TryGetValue(key, out var value) ? value : $"[{key}]";
            return arguments.Length > 0 ? string.Format(CultureInfo.CurrentCulture, template, arguments) : template;
        }
    }

    /// <summary>A small English seed covering representative report-server portal keys.</summary>
    public static MockTmLocalizer English() => new(new Dictionary<string, string>
    {
        ["ReportServer_Nav_Reports"] = "Reports",
        ["ReportServer_Nav_Favorites"] = "Favorites",
        ["ReportServer_Tenant"] = "Tenant",
        ["ReportServer_Favorites_EmptyTitle"] = "No favorites yet",
        ["ReportServer_NewReport_SourceRdl"] = "Import RDL",
        ["ReportServer_NewReport_RdlFile"] = "RDL file (.rdl, .xml)",
        ["ReportServer_NewReport_RdlImported"] = "Imported {0}.",
        ["ReportServer_NewReport_InvalidRdl"] = "The uploaded RDL could not be imported.",
        ["ReportServer_NewReport_RdlWarnings"] = "{0} RDL element(s) were not fully imported. Review the report after creating it.",
        ["ReportServer_NewReport_RdlRequired"] = "Upload an RDL file to continue.",
    });

    /// <summary>A small Czech seed covering the same representative keys.</summary>
    public static MockTmLocalizer Czech() => new(new Dictionary<string, string>
    {
        ["ReportServer_Nav_Reports"] = "Sestavy",
        ["ReportServer_Nav_Favorites"] = "Oblíbené",
        ["ReportServer_Tenant"] = "Nájemce",
        ["ReportServer_Favorites_EmptyTitle"] = "Zatím žádné oblíbené",
        ["ReportServer_NewReport_SourceRdl"] = "Importovat RDL",
        ["ReportServer_NewReport_RdlFile"] = "Soubor RDL (.rdl, .xml)",
        ["ReportServer_NewReport_RdlImported"] = "Importováno {0}.",
        ["ReportServer_NewReport_InvalidRdl"] = "Nahraný soubor RDL se nepodařilo importovat.",
        ["ReportServer_NewReport_RdlWarnings"] = "Některé prvky RDL se nepodařilo plně importovat (počet: {0}). Po vytvoření sestavu zkontrolujte.",
        ["ReportServer_NewReport_RdlRequired"] = "Pro pokračování nahrajte soubor RDL.",
    });
}
