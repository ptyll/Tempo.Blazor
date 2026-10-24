using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Tempo.ReportServer.Api.Security;
using Tempo.ReportServer.Web;
using Tempo.ReportServer.Web.Client;
using Tempo.ReportServer.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Without request localization the server leg renders in the OS culture of the machine hosting the
// app (e.g. Czech on a Czech Windows) regardless of the browser's Accept-Language — the prerendered
// chrome and every server-side string flip language per deployment. Pin the supported set to the
// library's built-in cultures and honour the browser's preference, falling back to English.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    CultureInfo[] supported =
    [
        new CultureInfo("en-US"), new CultureInfo("en"),
        new CultureInfo("cs"), new CultureInfo("sk"), new CultureInfo("fr"),
    ];
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supported;
    options.SupportedUICultures = supported;
});

var razorComponents = builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Cookie + OpenID Connect (Keycloak) BFF authentication with a server-side token store.
// No-op when "Authentication:Oidc" is unconfigured, so the self-contained demo keeps running.
var oidcOptions = builder.AddReportServerWebAuthentication();

// Serialize the host's authentication state into the WASM runtime so [Authorize]/AuthorizeView
// resolve identically in both legs of InteractiveAuto (claims only — never tokens). Only when auth
// is configured, since demo mode has no AuthenticationStateProvider to serialize.
if (oidcOptions.IsConfigured)
{
    razorComponents.AddAuthenticationStateSerialization();
}

// The typed ITempoReportServerClient is registered symmetrically for both runtimes in
// AddCommonServices (base URL from "Api:BaseUrl"). It attaches the bearer token per request from
// the scoped IAccessTokenProvider (ServerAccessTokenProvider here) via ApiClientBase — never through
// a server-side DelegatingHandler, whose cached factory scope could leak tokens across users.
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "ReportServerEmbeddingDemo",
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});
// Client-safe UI/data services shared with the WebAssembly leg (symmetric DI).
builder.Services.AddCommonServices(builder.Configuration);

// Server-only: the email Engine (Scriban/Mjml.Net/AngleSharp) renders the scheduled report e-mail. It is
// deliberately NOT part of AddCommonServices, which also runs in the WebAssembly leg (decision D11).
builder.Services.AddReportServerEmailDelivery();

// Host-only (server) services: report-server security + demo API key store.
builder.Services.AddSingleton<IReportApiKeyStore, DemoReportApiKeyStore>();
builder.Services.AddReportServerSecurity();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRequestLocalization();

UseProjectStaticAssets(app, "Tempo.Blazor.Reporting");
UseProjectStaticAssets(app, "Tempo.Blazor");
app.UseStaticFiles();
app.UseCors("ReportServerEmbeddingDemo");
if (oidcOptions.IsConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseAntiforgery();

app.MapReportServerDemoApi();
if (oidcOptions.IsConfigured)
{
    // Same-origin, cookie-authenticated token hand-out for the WASM leg (WasmAccessTokenProvider).
    // NOT a data proxy and NOT CORS-enabled: a cross-site script cannot read the response. The
    // refresh token never leaves the server — only a short-lived access token + its expiry.
    app.MapReportServerAuthTokenEndpoint();

    app.MapGet("/account/login", (string? returnUrl) => Results.Challenge(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
        [Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme]));
    // Sign-out clears the BFF cookie and redirects to the Keycloak end-session endpoint. Exposed as
    // both POST (form submit) and GET (the shell's sign-out link uses a full browser navigation from an
    // interactive component, where an antiforgery-token form is not available in the WASM leg).
    app.MapPost("/account/logout", () => Results.SignOut(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" },
        [
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
            Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
        ]));
    app.MapGet("/account/logout", (HttpContext httpContext) =>
    {
        // Reject cross-site GET triggers (logout CSRF, link prefetch, URL scanners). Browsers send
        // Sec-Fetch-Site: same-origin for same-site navigations and "none" for user-typed/bookmarked
        // ones; a cross-site <img>/prefetch sends "cross-site" (or "same-site" from a sibling origin).
        var fetchSite = httpContext.Request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.Ordinal) ||
            string.Equals(fetchSite, "same-site", StringComparison.Ordinal))
        {
            return Results.Redirect("/");
        }

        return Results.SignOut(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" },
            [
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
            ]);
    });
}
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Tempo.ReportServer.Web.Client._Imports).Assembly);

app.Run();

static void UseProjectStaticAssets(WebApplication app, string projectName)
{
    var assetRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", projectName, "wwwroot"));
    if (!Directory.Exists(assetRoot))
    {
        return;
    }

    var normalizedRoot = assetRoot.EndsWith(Path.DirectorySeparatorChar)
        ? assetRoot
        : assetRoot + Path.DirectorySeparatorChar;
    var requestPrefix = $"/_content/{projectName}";

    app.Use(async (context, next) =>
    {
        if (!HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var requestPath = context.Request.Path.Value;
        if (requestPath is null ||
            !requestPath.StartsWith(requestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var relativePath = requestPath[requestPrefix.Length..].TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = ContentTypeFor(fullPath);
        context.Response.ContentLength = new FileInfo(fullPath).Length;
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.SendFileAsync(fullPath).ConfigureAwait(false);
        }
    });
}

static string ContentTypeFor(string path)
    => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };

// The WebApplicationFactory host-localization tests reference the entry point explicitly.
public partial class Program;
