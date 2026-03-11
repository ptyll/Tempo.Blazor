using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI;
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

// Register Tempo.Blazor services (ITmLocalizer, ThemeService, ToastService)
builder.Services.AddTempoBlazor();

// Register Dashboard services
builder.Services.AddSingleton<IWidgetRegistry, InMemoryWidgetRegistry>();
builder.Services.AddScoped<IDashboardProvider, InMemoryDashboardProvider>();

// Register FluentValidation validators from Demo assembly
builder.Services.AddTempoFluentValidation(typeof(PersonFormValidator).Assembly);

var host = builder.Build();

// Apply persisted culture preference from localStorage before rendering
try
{
    var js = host.Services.GetRequiredService<IJSRuntime>();
    var storedCulture = await js.InvokeAsync<string?>("localStorage.getItem", "tm-demo-culture");
    if (!string.IsNullOrEmpty(storedCulture))
    {
        var culture = new CultureInfo(storedCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
catch
{
    // localStorage not available (e.g. during prerendering) – use default culture
}

await host.RunAsync();
