using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan 98c80bc3, phase 10b — the opt-in <c>Responsive</c> mode of TmChart: the viewBox follows the
/// measured plot box instead of the fixed 600 × 400, so a wide card is filled and the type keeps its
/// real size. The default (not responsive) stays byte-for-byte the pre-2.12.0 viewBox.
/// </summary>
public class TmChartResponsiveTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Charts/TmChart.razor.js";

    private static ChartData Data => new()
    {
        Labels = ["Jan", "Feb", "Mar", "Apr"],
        Datasets = [new ChartDataset { Label = "Sales", Values = [10, 20, 15, 30], Color = "#3b82f6" }]
    };

    private static double D(AngleSharp.Dom.IElement el, string name)
        => double.Parse(el.GetAttribute(name)!, CultureInfo.InvariantCulture);

    [Fact]
    public void Default_KeepsTheFixedSixHundredByFourHundredViewBox_AndNeverImportsTheModule()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, Data));

        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 600 400");
        cut.Find(".tm-chart__plot").HasAttribute("data-responsive").Should().BeFalse();
        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("import");
    }

    [Fact]
    public void Responsive_BeforeTheFirstMeasurement_RendersTheDefaultViewBox()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));

        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 600 400", "prerender and the first frame have no measurement yet");
        cut.Find(".tm-chart__plot").GetAttribute("data-responsive").Should().Be("true");
    }

    [Fact]
    public async Task Responsive_AfterAMeasurement_TheViewBoxIsThePlotBox()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(1200, 360));

        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 1200 360");
        // The plot now spans 1200 - PL - PR: the last bar group ends far beyond the old 580.
        var bars = cut.FindAll("rect.tm-chart__bar");
        (D(bars[^1], "x") + D(bars[^1], "width")).Should().BeGreaterThan(1000);
        cut.FindAll("line.tm-chart__grid-line").Max(l => D(l, "x2")).Should().BeApproximately(1180, 0.01);
    }

    [Fact]
    public async Task Responsive_MoreWidthMeansMoreCategoryLabels()
    {
        var labels = Enumerable.Range(0, 24).Select(i => $"{i:00}:00").ToArray();
        var data = new ChartData
        {
            Labels = labels,
            Datasets = [new ChartDataset { Label = "kW", Values = Enumerable.Range(0, 24).Select(i => (double)i).ToArray(), Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data)
            .Add(x => x.Responsive, true));
        var narrow = cut.FindAll("text.tm-chart__label").Count;

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(1400, 360));

        cut.FindAll("text.tm-chart__label").Count.Should().BeGreaterThan(narrow);
        cut.FindAll("text.tm-chart__label").Count.Should().Be(24, "1400 units hold all 24 hourly labels");
    }

    [Fact]
    public async Task Responsive_TinyOrInvalidMeasurements_AreClampedOrIgnored()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(40, 30));
        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 160 160", "a plot smaller than its own padding is clamped, never negative");

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(double.NaN, 300));
        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(-5, 300));
        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 160 160", "an invalid report is ignored");
    }

    [Fact]
    public async Task Responsive_SameMeasurementTwice_DoesNotRerender()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));
        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(900, 320));
        var renders = cut.RenderCount;

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(900, 320));

        cut.RenderCount.Should().Be(renders);
    }

    [Fact]
    public async Task Responsive_Pie_CentresTheDiscInTheMeasuredBox()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Pie)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(1000, 300));

        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 1000 300");
        var slice = cut.Find("path.tm-chart__slice").GetAttribute("d")!;
        slice.Should().StartWith("M 500,150", "the disc is centred in the measured 1000 × 300 box");
    }

    [Fact]
    public async Task Responsive_Funnel_KeepsItsDesignedViewBox()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Funnel)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));

        await cut.InvokeAsync(() => cut.Instance.OnPlotResized(1200, 400));

        cut.Find("svg").GetAttribute("viewBox").Should().Be("0 0 600 400",
            "the funnel's type scale is calibrated on the 600 × 400 box — Responsive does not apply to it");
    }

    [Fact]
    public async Task Responsive_Disposal_ReleasesTheObserver()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var observe = module.SetupVoid("observe", _ => true).SetVoidResult();
        module.SetupVoid("release", _ => true).SetVoidResult();

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));
        cut.WaitForAssertion(() => observe.Invocations.Should().NotBeEmpty());

        _ = cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());

        cut.WaitForAssertion(() => JSInterop.Invocations.Should().Contain(i => i.Identifier == "release"));
    }

    [Fact]
    public void WithoutAJsRuntime_ADefaultChartStillRenders()
    {
        // A host that renders components without any JS (an HtmlRenderer export, a console app) never registered
        // IJSRuntime: only a Responsive chart may ask for it.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(new MockTmLocalizer(new Dictionary<string, string> { ["TmChart_AriaLabel"] = "{0} chart" }));
        using var provider = services.BuildServiceProvider();
        using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var html = renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TmChart>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TmChart.Type)] = ChartType.Bar,
                [nameof(TmChart.Data)] = Data,
            }));
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();

        html.Should().Contain("viewBox=\"0 0 600 400\"");
    }

    [Fact]
    public async Task Responsive_AfterTheDataGoesEmptyAndComesBack_ObservesTheNewPlotBox()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var observe = module.SetupVoid("observe", _ => true).SetVoidResult();
        module.SetupVoid("release", _ => true).SetVoidResult();

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));
        cut.WaitForAssertion(() => observe.Invocations.Should().HaveCount(1));

        // The plot box is replaced by the empty state, then rebuilt: the old element is gone.
        cut.Render(p => p.Add(x => x.Data, new ChartData { Labels = [], Datasets = [] }));
        cut.WaitForAssertion(() => JSInterop.Invocations.Should().Contain(i => i.Identifier == "release"));
        cut.Render(p => p.Add(x => x.Data, Data));

        cut.WaitForAssertion(() => observe.Invocations.Should().HaveCount(2, "the rebuilt plot box is observed again"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Responsive_DisposedDuringAPendingImport_NeverObserves_AndReleasesTheModuleOnce()
    {
        var js = new PausedImportRuntime();
        Services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(js);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Data)
            .Add(x => x.Responsive, true));
        cut.WaitForAssertion(() => js.ImportRequested.Should().BeTrue());

        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        js.CompleteImport();

        (await Task.WhenAny(js.Module.Disposed.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(js.Module.Disposed.Task, "the import that outlived the chart is released");
        js.Module.DisposeCount.Should().Be(1);
        js.Module.Calls.Should().NotContain("observe", "a disposed chart must not start an observer");
        js.Module.Calls.Should().NotContain("release", "nothing was observed, so nothing is released");
    }

    [Fact]
    public async Task Responsive_WithoutAJsRuntime_KeepsTheDefaultViewBox()
    {
        // No JS runtime at all: a Responsive chart keeps the default 600 x 400 box for the lifetime of the instance.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(new MockTmLocalizer(new Dictionary<string, string> { ["TmChart_AriaLabel"] = "{0} chart" }));
        using var provider = services.BuildServiceProvider();
        using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TmChart>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TmChart.Type)] = ChartType.Bar,
                [nameof(TmChart.Data)] = Data,
                [nameof(TmChart.Responsive)] = true,
            }));
            return output.ToHtmlString();
        });

        html.Should().Contain("viewBox=\"0 0 600 400\"");
    }

    private sealed class PausedImportRuntime : Microsoft.JSInterop.IJSRuntime
    {
        private readonly TaskCompletionSource<Microsoft.JSInterop.IJSObjectReference> _import = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingModule Module { get; } = new();

        public bool ImportRequested { get; private set; }

        public void CompleteImport() => _import.SetResult(Module);

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            if (identifier == "import")
            {
                ImportRequested = true;
                return new ValueTask<T>(_import.Task.ContinueWith(t => (T)(object)t.Result));
            }

            return new ValueTask<T>(default(T)!);
        }

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<T>(identifier, args);
    }

    private sealed class RecordingModule : Microsoft.JSInterop.IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public int DisposeCount { get; private set; }

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return new ValueTask<T>(default(T)!);
        }

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<T>(identifier, args);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}