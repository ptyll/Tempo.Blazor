using System.Reflection;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Localization;
using Tempo.Blazor.Demo.SharedUI.Services;
using Tempo.Blazor.Demo.Validators;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.FluentValidation;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Reporting.Configuration;
using Tempo.Blazor.Services;

// Import Components namespace for App.razor
using Components = Tempo.Blazor.Demo.InteractiveAuto.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// HttpClient for API calls. In Development the primary handler accepts the (typically untrusted)
// ASP.NET Core developer certificate: these are SERVER-side calls where the browser-style
// IgnoreHTTPSErrors does not apply, and a fresh machine would otherwise 500 every page whose
// prerender touches the API. See DemoApiHttpHandler.
var acceptDeveloperCertificate = builder.Environment.IsDevelopment();
builder.Services.AddScoped(sp => new HttpClient(DemoApiHttpHandler.Create(acceptDeveloperCertificate))
{
    BaseAddress = new Uri(builder.Configuration["DemoApi:BaseUrl"] ?? "https://localhost:5100")
});

builder.Services.AddHttpClient("DemoApi", c =>
    c.BaseAddress = new Uri(builder.Configuration["DemoApi:BaseUrl"] ?? "https://localhost:5100"))
    .ConfigurePrimaryHttpMessageHandler(() => DemoApiHttpHandler.Create(acceptDeveloperCertificate));

// Register SharedUI services
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
// Demo-only strings (TmDemo_*, *Demo_* pages) come from SharedUI's DemoResources; everything
// else falls through to the library's TmResources. The Client (WASM) side registers the same
// decorator in its own Program.cs.
builder.Services.AddTempoDemoLocalizer();
builder.Services.AddScoped<Tempo.Blazor.Demo.SharedUI.Layout.ColorThemeState>();
builder.Services.AddTempoBlazorPdfViewer();
builder.Services.AddTempoBlazorCodes();
builder.Services.AddTempoBlazorAI();
builder.Services.AddTempoBlazorDocumentEditor();
builder.Services.AddTempoBlazorDiagramEditor();
builder.Services.AddTempoBlazorWireframe();
builder.Services.AddTempoBlazorModeling();
// Second demo modeling source (phase 3): the header provider select lists both keys.
builder.Services.AddSingleton<IModelingModelProvider, Tempo.Blazor.Demo.SharedUI.Services.DemoUmlModelingModelProvider>();
builder.Services.AddTempoBlazorSpreadsheet();
builder.Services.AddTempoBlazorGanttXlsx();
builder.Services.AddTempoBlazorDataTableXlsx();
builder.Services.AddTempoBlazorNotionEditor();
builder.Services.AddTempoBlazorSigning();
builder.Services.AddTempoBlazorReporting();
// Email template editor (Fáze 11): the EDITOR only — no Scriban/Mjml.Net/AngleSharp in the host — plus the
// typed API client and the HTTP IEmailPreviewRenderer. The preview and the HTML export render on the demo
// API through IEmailPreviewRenderer, which is the pattern a real host uses (contract §3.0/§3.5). Registered
// in every demo host so the editor is proven in all three render modes (WASM / Server / InteractiveAuto).
builder.Services.AddTempoEmailTemplateEditor();
builder.Services.AddScoped<Tempo.Blazor.Demo.Services.IEmailTemplateApiClient, Tempo.Blazor.Demo.Services.EmailTemplateApiClient>();
builder.Services.AddScoped<Tempo.Blazor.EmailTemplates.Abstractions.Rendering.IEmailPreviewRenderer,
    Tempo.Blazor.Demo.Services.HttpEmailPreviewRenderer>();

builder.Services.AddInMemoryNotifications();
builder.Services.AddScoped<DemoNotionNotificationService>();
builder.Services.AddScoped<ITmNotificationService>(sp => sp.GetRequiredService<DemoNotionNotificationService>());
builder.Services.AddScoped<DemoReportEmbeddingSourceFactory>();

// Register Dashboard services
builder.Services.AddSingleton<IWidgetRegistry, InMemoryWidgetRegistry>();
builder.Services.AddScoped<IDashboardProvider, InMemoryDashboardProvider>();

// Register FluentValidation validators from Demo assembly
builder.Services.AddTempoFluentValidation(typeof(PersonFormValidator).Assembly);

var app = builder.Build();

// The demo's default language is English — the neutral culture of both resource tables
// (TmResources and DemoResources). Without an explicit default, .NET seeds request threads from the
// ambient OS culture, so a machine whose regional format is not English (e.g. cs-CZ) prerenders (and
// serves through the initial interactive-server leg) every string in that language, and E2E results
// depend on the machine running the host (docs/e2e-test-lanes.md pins LANG=en_US.UTF-8 on Linux for
// exactly this reason; Windows has no per-process equivalent). The WebAssembly leg is unaffected: it
// picks its culture in the browser (localStorage preference → navigator language) via the Client
// Program.cs.
var demoDefaultCulture = new System.Globalization.CultureInfo("en");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = demoDefaultCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = demoDefaultCulture;

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
// N193: dedicated readiness endpoint for the E2E host probe — /health must answer 2xx only when
// the app is actually serving; a crashed app answering 500 on "/" used to read as "reachable".
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    version = typeof(Program).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown",
}));

app.MapRazorComponents<Components.App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(Tempo.Blazor.Demo.InteractiveAuto.Client._Imports).Assembly,
        typeof(Tempo.Blazor.Demo.SharedUI.Pages.Home).Assembly);

app.Run();
