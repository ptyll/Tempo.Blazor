using Tempo.Blazor.Configuration;
using Tempo.Blazor.Demo.Services;
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

// HttpClient for API calls
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["DemoApi:BaseUrl"] ?? "https://localhost:5100")
});

builder.Services.AddHttpClient("DemoApi", c =>
    c.BaseAddress = new Uri(builder.Configuration["DemoApi:BaseUrl"] ?? "https://localhost:5100"));

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

// The WASM demo reads the persisted culture from localStorage; a server host cannot see it, so
// LanguageSwitcher also writes a tm-demo-culture cookie. Apply it here (single-user demo — setting
// the process defaults is fine) so the Server leg honours the language switch too.
app.Use(async (context, next) =>
{
    if (context.Request.Cookies.TryGetValue("tm-demo-culture", out var cultureName)
        && !string.IsNullOrWhiteSpace(cultureName))
    {
        try
        {
            var culture = new System.Globalization.CultureInfo(cultureName);
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // Unknown cookie value — keep the default culture.
        }
    }

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

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
