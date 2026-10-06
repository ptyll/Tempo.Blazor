using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI;
using Tempo.Blazor.Demo.SharedUI.Services;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Reporting.Configuration;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Demo.Validators;
using Tempo.Blazor.FluentValidation;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddHttpClient("DemoApi", c =>
    c.BaseAddress = new Uri("https://localhost:5100"));

builder.Services.AddScoped<PersonHttpDataProvider>();
builder.Services.AddScoped<ActivityHttpService>();
builder.Services.AddScoped<AttachmentHttpProvider>();
builder.Services.AddScoped<ImageHttpGalleryProvider>();
builder.Services.AddScoped<ViewHttpProvider>();
builder.Services.AddScoped<DemoDocumentEditorProvider>();
builder.Services.AddScoped<DemoDocumentCollaborationProvider>();
builder.Services.AddScoped(sp =>
{
    var baseUri = sp.GetRequiredService<IHttpClientFactory>().CreateClient("DemoApi").BaseAddress!.ToString().TrimEnd('/');
    return new SignalRDocumentCollaborationProvider($"{baseUri}/hubs/document-editor-collaboration");
});
builder.Services.AddScoped<DemoDocumentSuggestionProvider>();
builder.Services.AddScoped<DemoDocumentFormatProvider>();
builder.Services.AddScoped<DemoDocumentPdfExportProvider>();
builder.Services.AddScoped<DemoDocumentComparisonProvider>();
builder.Services.AddScoped<DemoDocumentImageUrlResolver>();
builder.Services.AddScoped<DemoDocumentTokenProvider>();
builder.Services.AddScoped<DemoMentionProvider>();
builder.Services.AddScoped<DemoNotionDataProvider>();
builder.Services.AddScoped<DemoNotionAggregateProvider>();
builder.Services.AddScoped<DemoNotionMediaLibraryProvider>();
builder.Services.AddScoped<DemoNotionFileProvider>();
builder.Services.AddScoped<DemoNotionTokenProvider>();
builder.Services.AddScoped<DemoNotionAIProvider>();
builder.Services.AddScoped<DemoNotionTaskProvider>();
builder.Services.AddScoped<DemoNotionReactionProvider>();
builder.Services.AddScoped<DemoNotionAnalyticsProvider>();
builder.Services.AddScoped<DemoNotionPagePropertiesProvider>();
builder.Services.AddScoped<DemoNotionTemplateProvider>();
builder.Services.AddScoped<DemoNotionSpaceProvider>();
builder.Services.AddScoped<DemoNotionBlogProvider>();
builder.Services.AddScoped<DemoNotionWatchProvider>();
builder.Services.AddScoped<DemoNotionPermissionProvider>();
builder.Services.AddScoped<DemoNotionPublicShareProvider>();
builder.Services.AddScoped<DemoNotionAuditProvider>();
builder.Services.AddScoped<DemoSmartLinkProvider>();
builder.Services.AddScoped<DemoNotionDatabaseProvider>();
builder.Services.AddTmWorkItemProvider<DemoWorkItemProvider>();
builder.Services.AddTmWorkItemProvider<DemoOpsWorkItemProvider>();
builder.Services.AddScoped<DemoSharedWorkItemProvider>();
builder.Services.AddScoped<ITmWorkItemProvider>(sp => sp.GetRequiredService<DemoSharedWorkItemProvider>());
builder.Services.AddScoped<MockNotionDatabaseProvider>();
builder.Services.AddScoped<MockNotionCommentProvider>();
builder.Services.AddScoped<MockNotionHistoryProvider>();
builder.Services.AddScoped<DemoNotionHistoryProvider>();
builder.Services.AddScoped<MockNotionMentionProvider>();
builder.Services.AddScoped<MockNotionSearchProvider>();
builder.Services.AddScoped<MockNotionWireframeDocumentProvider>();
builder.Services.AddScoped<MockNotionDiagramDocumentProvider>();
builder.Services.AddScoped<ApiSpreadsheetDocumentProvider>();
builder.Services.AddScoped<ApiWireframeDocumentProvider>();
builder.Services.AddScoped<ApiDiagramDocumentProvider>();
builder.Services.AddScoped<Tempo.Blazor.DocumentLibrary.ITempoDocumentLibraryProvider, ApiTempoDocumentLibraryProvider>();
builder.Services.AddScoped<Tempo.Blazor.DocumentLibrary.ITempoDocumentChangeNotifier>(sp =>
{
    var baseUri = sp.GetRequiredService<IHttpClientFactory>().CreateClient("DemoApi").BaseAddress!.ToString().TrimEnd('/');
    return new Tempo.Blazor.DocumentLibrary.Collaboration.SignalRTempoDocumentChangeNotifier($"{baseUri}/hubs/document-library");
});
builder.Services.AddScoped<DemoNotionImportExportProvider>();
builder.Services.AddScoped<SignalRCollaborationProvider>();

// Register Tempo.Blazor services (ITmLocalizer, ThemeService, ToastService)
builder.Services.AddTempoBlazor();
builder.Services.AddScoped<Tempo.Blazor.Demo.SharedUI.Layout.ColorThemeState>();
builder.Services.AddTempoBlazorPdfViewer();
builder.Services.AddTempoBlazorCodes();
builder.Services.AddTempoBlazorDocumentEditor();
builder.Services.AddTempoBlazorDiagramEditor();
builder.Services.AddTempoBlazorWireframe();
builder.Services.AddTempoBlazorModeling();
builder.Services.AddTempoBlazorSpreadsheet();
builder.Services.AddTempoBlazorGanttXlsx();
builder.Services.AddTempoBlazorDataTableXlsx();
builder.Services.AddTempoBlazorNotionEditor();
builder.Services.AddTempoBlazorSigning();
builder.Services.AddTempoBlazorReporting();
builder.Services.AddSingleton<IModelingNotationProfile, ErdNotationProfile>();
builder.Services.AddInMemoryNotifications();
builder.Services.AddScoped<DemoNotionNotificationService>();
builder.Services.AddScoped<ITmNotificationService>(sp => sp.GetRequiredService<DemoNotionNotificationService>());

// Register Dashboard services
builder.Services.AddSingleton<IWidgetRegistry, InMemoryWidgetRegistry>();
builder.Services.AddScoped<IDashboardProvider, InMemoryDashboardProvider>();

// Register FluentValidation validators from Demo assembly
builder.Services.AddTempoFluentValidation(typeof(PersonFormValidator).Assembly);

// Email template editor demo: engine + localization, and the typed API client.
builder.Services.AddTempoEmailTemplates();
builder.Services.AddScoped<Tempo.Blazor.Demo.Services.IEmailTemplateApiClient, Tempo.Blazor.Demo.Services.EmailTemplateApiClient>();
builder.Services.AddScoped<DemoReportEmbeddingSourceFactory>();

// The renderer captures its execution context, culture included, when the host is built. Reading
// the preference here, before that, is the only point a persisted culture reaches component renders.
try
{
    var early = Tempo.Blazor.Demo.EarlyCulture.ReadStoredCulture("tm-demo-culture");
    if (!string.IsNullOrEmpty(early))
    {
        var earlyCulture = new CultureInfo(early);
        CultureInfo.DefaultThreadCurrentCulture = earlyCulture;
        CultureInfo.DefaultThreadCurrentUICulture = earlyCulture;
        CultureInfo.CurrentCulture = earlyCulture;
        CultureInfo.CurrentUICulture = earlyCulture;
    }
}
catch
{
    // localStorage is unavailable this early (prerender) — the post-build read below still applies it.
}

var host = builder.Build();

// Initialize E2E test helper
DemoJsInterop.Initialize(host.Services.GetRequiredService<ITmNotificationService>());

// Apply persisted culture preference from localStorage before rendering
try
{
    var js = host.Services.GetRequiredService<IJSRuntime>();
    var storedCulture = await js.InvokeAsync<string?>("localStorage.getItem", "tm-demo-culture");
    if (!string.IsNullOrEmpty(storedCulture))
    {
        var culture = new CultureInfo(storedCulture);
        // DefaultThread* only reaches threads started afterwards. Blazor WebAssembly renders on this
        // thread, so the current culture has to move too or the switch never changes a string.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // The renderer does not inherit the startup thread's culture, so the layout reapplies this.
        Tempo.Blazor.Demo.SharedUI.Layout.DemoCulture.Applied = culture;
    }
}
catch
{
    // localStorage not available (e.g. during prerendering) – use default culture
}

await host.RunAsync();
