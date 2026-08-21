using Microsoft.EntityFrameworkCore;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Api.Endpoints;
using Tempo.Blazor.Demo.Api.Hubs;
using Tempo.Blazor.Demo.Api.Services;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.DocumentFormats.HeadlessLayout;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.Mcp;
using Tempo.Blazor.Mcp.DocumentEditor;
using Tempo.Blazor.Models;
using Tempo.Blazor.WebPush;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// `diagrams.db` next to the project is COMMITTED, so anything that boots this host mutates the
// working tree — the e2e lane leaves it modified plus a -wal/-shm pair every run. Overridable via
// `Demo__DiagramsDbPath`, which the Playwright host launcher points at a temp directory; unset it
// keeps the committed file so a hand-started demo still has its seeded diagrams.
var dbPath = builder.Configuration["Demo:DiagramsDbPath"] is { Length: > 0 } configuredDbPath
    ? Path.GetFullPath(configuredDbPath)
    : Path.Combine(builder.Environment.ContentRootPath, "diagrams.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<DemoDiagramDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(
        "http://localhost:5010",
        "https://localhost:7106")
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));   // required for SignalR WebSocket handshake

builder.Services.AddSignalR();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton<MockPersonStore>();
builder.Services.AddSingleton<MockUserStore>();
builder.Services.AddSingleton<MockActivityStore>();
builder.Services.AddSingleton<MockAttachmentStore>();
builder.Services.AddSingleton<MockImageStore>();
builder.Services.AddSingleton<MockViewStore>();
builder.Services.AddSingleton<MockDropdownStore>();
builder.Services.AddSingleton<MockScheduleStore>();
builder.Services.AddSingleton<MockGanttStore>();
builder.Services.AddSingleton<MockTokenStore>();
builder.Services.AddSingleton<MockWireframeStore>();
builder.Services.AddSingleton<MockNotionDataStore>();
builder.Services.AddSingleton<MockNotionBlockStore>();
builder.Services.AddSingleton<DemoNotionAggregateStore>();
builder.Services.AddSingleton<MockNotionBookmarkStore>();
builder.Services.AddSingleton<DemoWorkItemStore>();
builder.Services.AddSingleton<DemoNotionSearchService>();
builder.Services.AddSingleton<DemoNotionImportExportProvider>();
builder.Services.AddSingleton<MockNotionAnalyticsStore>();
builder.Services.AddSingleton<MockNotionReactionStore>();
builder.Services.AddSingleton<DemoNotionAuditProvider>();
builder.Services.AddSingleton<DemoNotionBlogProvider>();
builder.Services.AddSingleton<DemoNotionTemplateStore>();
builder.Services.AddSingleton<DemoNotionNotificationStore>();
builder.Services.AddSingleton<DemoNotionPermissionProvider>();
builder.Services.AddSingleton<DemoNotionPublicShareProvider>();
builder.Services.AddSingleton<DemoNotionWatchProvider>();
builder.Services.AddSingleton<DemoNotionTaskProvider>();
builder.Services.AddSingleton<DemoNotionHistoryStore>();
builder.Services.AddSingleton<MockSpreadsheetDocumentStore>();
builder.Services.AddSingleton<Tempo.Blazor.DocumentLibrary.ITempoDocumentChangePublisher,
    Tempo.Blazor.Demo.Api.Services.HubTempoDocumentChangePublisher>();
builder.Services.AddSingleton<DocumentLibraryStore>();
builder.Services.AddSingleton<DocumentLibrarySeeder>();

// Store-backed providers + MCP wireframe tools (the tools run inside this API over the same store).
builder.Services.AddSingleton<Tempo.Blazor.DocumentLibrary.ITempoDocumentLibraryProvider,
    Tempo.Blazor.Demo.Api.Services.StoreDocumentLibraryProvider>();
builder.Services.AddSingleton<Tempo.Blazor.NotionEditor.Interfaces.IWireframeDocumentProvider,
    Tempo.Blazor.Demo.Api.Services.StoreWireframeDocumentProvider>();
builder.Services.AddSingleton<Tempo.Reporting.Abstractions.Data.IReportDefinitionStore,
    Tempo.Reporting.Abstractions.Data.InMemoryReportDefinitionStore>();
builder.Services.AddTempoWireframeMcpTools();
// Component registry + headless server-side SVG renderer (IWireframeSvgRenderer).
builder.Services.AddTempoBlazorWireframe();
builder.Services.AddTempoReportingMcpTools();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (Tempo.Blazor.DocumentLibrary.TempoDocumentConflictException ex)
            {
                return new ModelContextProtocol.Protocol.CallToolResult
                {
                    Content = [new ModelContextProtocol.Protocol.TextContentBlock
                        { Text = Tempo.Blazor.Mcp.McpToolResults.Failure(Tempo.Blazor.Mcp.McpToolResults.Conflict, ex.Message) }],
                    IsError = false
                };
            }
            catch (Exception ex)
            {
                return new ModelContextProtocol.Protocol.CallToolResult
                {
                    Content = [new ModelContextProtocol.Protocol.TextContentBlock
                        { Text = Tempo.Blazor.Mcp.McpToolResults.Failure(Tempo.Blazor.Mcp.McpToolResults.Error, ex.Message) }],
                    IsError = false
                };
            }
        });
    })
    .WithToolsFromAssembly(typeof(Tempo.Blazor.Mcp.TempoWireframeMcp).Assembly);
builder.Services.AddSingleton<MockNotionDatabaseStore>();
builder.Services.AddSingleton<DemoDocumentEditorStore>();
// The document MCP tools (document_editor_* / document_render_*) resolve the same store, so
// agent edits and the editor demo share one document state.
builder.Services.AddSingleton<Tempo.Blazor.DocumentEditor.Interfaces.IDocumentEditorProvider>(
    sp => sp.GetRequiredService<DemoDocumentEditorStore>());
builder.Services.AddSingleton<DemoDocumentFormatProvider>();
builder.Services.AddTempoDocumentServices();
// Font catalog + limits for document_render_preview / document_render_pdf (system Arial/DejaVu
// fallback incl. the Aptos alias — mirrors DemoDocumentExportFontCatalog).
builder.Services.AddTempoDocumentEditorMcpRendering();
// Opt-in live co-editing: MCP edits broadcast through the shared collaboration store AND are
// pushed to the SignalR document groups, so open TmDocumentEditor sessions see agent edits live.
builder.Services.AddTempoDocumentEditorMcpCollaboration(options =>
{
    options.Enabled = true;
    options.AgentName = "MCP Agent";
});
builder.Services.AddSingleton<Tempo.Blazor.Demo.Api.Services.McpCollaborationSignalRForwarder>();
builder.Services.AddSingleton<DemoDocumentExportFontCatalog>();
builder.Services.AddSingleton<DemoDocumentPdfExportProvider>();
builder.Services.AddSingleton<DemoDocumentPdfExportCache>();
builder.Services.AddSingleton<DemoDocumentComparisonProvider>();
builder.Services.AddSingleton<InMemoryDocumentCollaborationProvider>();
builder.Services.AddSingleton<Tempo.Blazor.DocumentEditor.Interfaces.IDocumentCollaborationProvider>(
    sp => sp.GetRequiredService<InMemoryDocumentCollaborationProvider>());
builder.Services.AddSingleton<InMemoryDocumentSuggestionProvider>();
builder.Services.AddSingleton<IDiagramExportService, DemoDiagramExportService>();
builder.Services.AddSingleton<WireframeExportService>();
builder.Services.AddScoped<DemoDiagramHistoryStore>();
builder.Services.AddScoped<IDiagramHistoryStore>(sp => sp.GetRequiredService<DemoDiagramHistoryStore>());

// Email templates: engine + validators + localization, demo store and SMTP delivery (smtp4dev).
builder.Services.AddLocalization();
builder.Services.AddTempoEmailTemplateEngine();
builder.Services.AddSingleton<DemoEmailTemplateStore>();
builder.Services.AddSingleton<Tempo.Blazor.EmailTemplates.Abstractions.Contracts.IEmailTemplateStore>(
    sp => sp.GetRequiredService<DemoEmailTemplateStore>());
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<ISmtpClientFactory, MailKitSmtpClientFactory>();
builder.Services.AddSingleton<Tempo.Blazor.EmailTemplates.Abstractions.Contracts.IEmailSender, SmtpEmailSender>();

// ── Notifications: real-time SignalR backend + Web Push + daily digest ────
builder.Services.AddSingleton<Tempo.Blazor.Services.InMemoryNotificationStore>();
builder.Services.AddSingleton<Tempo.Blazor.Abstractions.Shared.ITmNotificationService>(sp =>
    new SignalRNotificationBroadcaster(
        sp.GetRequiredService<Tempo.Blazor.Services.InMemoryNotificationStore>(),
        sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<TmNotificationHub>>()));
builder.Services.AddSingleton<Tempo.Blazor.Abstractions.Interfaces.IPushSubscriptionStore,
    Tempo.Blazor.Services.InMemoryPushSubscriptionStore>();

// VAPID: use configured keys, otherwise generate an ephemeral pair for the demo run.
builder.Services.Configure<WebPushOptions>(builder.Configuration.GetSection("WebPush"));
builder.Services.PostConfigure<WebPushOptions>(o =>
{
    if (!o.IsConfigured)
    {
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        o.PublicKey = keys.PublicKey;
        o.PrivateKey = keys.PrivateKey;
    }
});
builder.Services.AddSingleton<Tempo.Blazor.Abstractions.Interfaces.IWebPushSender, VapidWebPushSender>();

builder.Services.Configure<Tempo.Blazor.Abstractions.Shared.TmNotificationDigestOptions>(
    builder.Configuration.GetSection("NotificationDigest"));
builder.Services.AddSingleton<Tempo.Blazor.Abstractions.Interfaces.INotificationRecipientSource,
    DemoNotificationRecipientSource>();
builder.Services.AddSingleton<Tempo.Blazor.Abstractions.Interfaces.INotificationDigestSender,
    SmtpNotificationDigestSender>();
builder.Services.AddSingleton<TmNotificationDigestService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TmNotificationDigestService>());

var app = builder.Build();

// Materialize the MCP→SignalR collaboration forwarder so agent edits reach open editors live.
app.Services.GetRequiredService<Tempo.Blazor.Demo.Api.Services.McpCollaborationSignalRForwarder>();

app.UseCors();

app.MapPersonEndpoints();
app.MapUserEndpoints();
app.MapActivityEndpoints();
app.MapAttachmentEndpoints();
app.MapImageEndpoints();
app.MapViewEndpoints();
app.MapDropdownEndpoints();
app.MapScheduleEndpoints();
app.MapGanttEndpoints();
app.MapImportExportEndpoints();
app.MapTokenEndpoints();
app.MapWireframeEndpoints();
app.MapWireframeExportEndpoints();
app.MapWireframePreviewEndpoints();
app.MapDiagramExportEndpoints();
app.MapDiagramHistoryEndpoints();
app.MapNotionEditorEndpoints();
app.MapDocumentLibraryEndpoints();
app.Services.GetRequiredService<DocumentLibrarySeeder>().EnsureSeeded();
app.MapDatabaseEndpoints();
app.MapDocumentEditorEndpoints();
app.MapLanguageToolEndpoints();
app.MapEmailTemplateEndpoints();
app.MapHub<DocumentEditorCollaborationHub>("/hubs/document-editor-collaboration");
app.MapHub<NotionCollaborationHub>("/hubs/notion-collaboration");
app.MapHub<TempoDocumentChangeHub>("/hubs/document-library");
app.MapHub<TmNotificationHub>("/hubs/notifications");
app.MapNotificationEndpoints();
app.MapMcp("/mcp");

// Schema creation goes through DemoDiagramSchema because this host is started CONCURRENTLY against one
// database — many WebApplicationFactory hosts inside one test process, and the demo as its own process
// beside them from the e2e lane. A bare EnsureCreated() there is a check-then-act, and the host that loses
// gets 'table "DiagramSnapshots" already exists' at startup; see that type for the measurement.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DemoDiagramDbContext>();
    DemoDiagramSchema.EnsureCreated(db, dbPath);
}

app.Run();

public partial class Program { }
