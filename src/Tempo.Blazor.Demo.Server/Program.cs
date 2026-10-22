using System.Reflection;
using Tempo.Blazor.Configuration;
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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

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
builder.Services.AddScoped<SignalRCollaborationProvider>();
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

// Register Tempo.Blazor services (ITmLocalizer, ThemeService, ToastService)
builder.Services.AddTempoBlazor();
// Since 2.9.1 the relative-time stamps read the browser's time zone by default (ITmUserTimeZone,
// tm-user-zone.js); the former AddTempoUserTimeZone() call is an obsolete no-op alias.
// Demo-only strings (TmDemo_*, *Demo_* pages) come from SharedUI's DemoResources; everything
// else falls through to the library's TmResources.
builder.Services.AddTempoDemoLocalizer();
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
// ambient OS culture, so a machine whose regional format is not English (e.g. cs-CZ) renders every
// server-side string in that language and E2E results depend on the machine running the host
// (docs/e2e-test-lanes.md pins LANG=en_US.UTF-8 on Linux for exactly this reason; Windows has no
// per-process equivalent). Pin English here; the cookie below still overrides it per user.
var demoDefaultCulture = new System.Globalization.CultureInfo("en");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = demoDefaultCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = demoDefaultCulture;

// The WASM demo reads the persisted culture from localStorage; a server host cannot see it, so
// LanguageSwitcher also writes a tm-demo-culture cookie. Apply the cookie choice here — or, when a
// request carries no cookie, converge back to the English default. (Single-user demo, so setting
// the process defaults is fine — but "no preference" must mean the default again: leaving the last
// switch stuck process-wide made every later cookie-less request render in the previous visitor's
// language, which turned server-side E2E results into a function of test order.)
app.Use(async (context, next) =>
{
    System.Globalization.CultureInfo applied = demoDefaultCulture;
    if (context.Request.Cookies.TryGetValue("tm-demo-culture", out var cultureName)
        && !string.IsNullOrWhiteSpace(cultureName))
    {
        try
        {
            applied = new System.Globalization.CultureInfo(cultureName);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // Unknown cookie value — keep the default culture.
        }
    }

    System.Globalization.CultureInfo.DefaultThreadCurrentCulture = applied;
    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = applied;
    System.Globalization.CultureInfo.CurrentCulture = applied;
    System.Globalization.CultureInfo.CurrentUICulture = applied;

    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// N193: dedicated readiness endpoint for the E2E host probe — /health must answer 2xx only when
// the app is actually serving; a crashed app answering 500 on "/" used to read as "reachable".
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    version = typeof(Program).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown",
}));

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
