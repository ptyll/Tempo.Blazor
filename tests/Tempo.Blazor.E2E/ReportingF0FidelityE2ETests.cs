using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;
using Tempo.Reporting.Engine.Fonts;
using Tempo.Reporting.Engine.Snapshot;

namespace Tempo.Blazor.E2E;

/// <summary>F0 reporting fidelity gate for C# metric tables versus browser canvas measurements.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F0")]
[DoNotParallelize]
public sealed class ReportingF0FidelityE2ETests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task F0_ReportingHarness_MatchesBrowserMeasureTextAndCapturesFidelityScreenshot()
    {
        var fonts = LoadF0Fonts();
        var sampleRequests = BuildSampleRequests();

        await using var host = ReportingStaticHost.Start(FindRepositoryRoot(), fonts.ToStaticFiles());
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        var page = await browserContext.NewPageAsync();
        await page.GotoAsync($"{host.BaseUrl}/reporting-harness.html", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await PlaywrightTestBase.WaitForPredicateAsync(page, "() => window.__tempoReportingHarness?.ready === true", 30_000);
        var fontFaces = fonts.ToFontFaces(host.BaseUrl);
        var fontFetchProbeJson = await page.EvaluateAsync<string>(
            """
            async faces => {
                const results = [];
                for (const face of faces) {
                    const family = face.family || face.Family || face.fontFamily || face.FontFamily || '';
                    const url = face.url || face.Url || '';
                    try {
                        const response = await fetch(url);
                        const bytes = await response.arrayBuffer();
                        results.push({ family, url, ok: response.ok, status: response.status, byteLength: bytes.byteLength, message: '' });
                    } catch (error) {
                        results.push({ family, url, ok: false, status: 0, byteLength: 0, message: `${error?.name || 'Error'}: ${error?.message || error}` });
                    }
                }

                return JSON.stringify(results);
            }
            """,
            fontFaces);
        var fontFetchProbe = JsonSerializer.Deserialize<List<FontFetchProbe>>(fontFetchProbeJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        Assert.IsTrue(fontFetchProbe.All(static probe => probe.Ok && probe.ByteLength > 0), JsonSerializer.Serialize(fontFetchProbe, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var calibrationJson = await page.EvaluateAsync<string>(
            """
            async args => {
                await window.__tempoReportingHarness.loadFonts(args.fontFaces);
                const canvas = document.createElement('canvas');
                const context = canvas.getContext('2d');
                if ('fontKerning' in context) {
                    context.fontKerning = 'none';
                }

                const groups = new Map();
                for (const sample of args.samples || []) {
                    const family = sample.fontFamily || sample.FontFamily || 'sans-serif';
                    const weight = sample.fontWeight || sample.FontWeight || '400';
                    const style = sample.fontStyle || sample.FontStyle || 'normal';
                    const pixelsPerEm = Math.round(Number(sample.fontSize || sample.FontSize || 12));
                    const key = `${family}\u001f${weight}\u001f${style}\u001f${pixelsPerEm}`;
                    if (!groups.has(key)) {
                        groups.set(key, { family, weight, style, pixelsPerEm, codePoints: new Set() });
                    }

                    for (const glyph of Array.from(sample.text || sample.Text || '')) {
                        groups.get(key).codePoints.add(glyph.codePointAt(0));
                    }
                }

                const results = [];
                for (const group of groups.values()) {
                    context.font = `${group.style} ${group.weight} ${group.pixelsPerEm}px "${group.family}"`;
                    for (const codePoint of group.codePoints.values()) {
                        const width = context.measureText(String.fromCodePoint(codePoint)).width;
                        results.push({
                            family: group.family,
                            weight: group.weight,
                            style: group.style,
                            pixelsPerEm: group.pixelsPerEm,
                            codePoint,
                            width: Math.max(0, Math.round(width))
                        });
                    }
                }

                return JSON.stringify(results);
            }
            """,
            new
            {
                fontFaces,
                samples = sampleRequests
            });
        var calibration = JsonSerializer.Deserialize<List<FontHintCalibration>>(calibrationJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        var metricTable = new FontMetricTable(
            [
                ApplyHintedCalibration(fonts.SansFace, calibration),
                ApplyHintedCalibration(fonts.SansBoldFace, calibration),
                ApplyHintedCalibration(fonts.CjkFace, calibration)
            ],
            "Tempo F0 Sans",
            ["Tempo F0 CJK"]);
        ITextMeasurer textMeasurer = new TableTextMeasurer(metricTable);
        var samples = BuildSamples(sampleRequests, textMeasurer);
        var snapshot = BuildSnapshot(samples);
        var snapshotJson = ReportSnapshotJsonSerializer.Serialize(snapshot);

        var loadResult = await page.EvaluateAsync<ReportingHarnessLoadResult>(
            """
            async args => {
                try {
                    return {
                        ok: true,
                        summary: await window.__tempoReportingHarness.loadSnapshot(JSON.parse(args.snapshotJson), args.fontFaces),
                        message: ''
                    };
                } catch (error) {
                    return {
                        ok: false,
                        summary: null,
                        message: `${error?.name || 'Error'}: ${error?.message || error}`
                    };
                }
            }
            """,
            new
            {
                snapshotJson,
                fontFaces
            });
        Assert.IsTrue(loadResult.Ok, loadResult.Message);
        var summary = loadResult.Summary ?? throw new AssertFailedException("Reporting harness did not return a summary.");

        Assert.AreEqual(samples.Count, summary.TextRunCount, "Harness must paint every C# sample text run.");
        Assert.IsTrue(summary.PaintedCommandCount >= samples.Count + 1, "Harness must paint page primitives plus text runs.");

        var browserMeasurementsJson = await page.EvaluateAsync<string>(
            """
            async samples => JSON.stringify(await window.__tempoReportingHarness.measureSamples(samples))
            """,
            samples.Select(static sample => new BrowserMeasureRequest
            {
                Id = sample.Id,
                Text = sample.Text,
                FontFamily = sample.FontFamily,
                FontSize = sample.FontSize,
                FontWeight = sample.Bold ? "700" : "400",
                FontStyle = sample.Italic ? "italic" : "normal",
                LetterSpacing = sample.LetterSpacing,
                Kerning = false
            }).ToArray());
        var browserMeasurements = JsonSerializer.Deserialize<List<BrowserMeasurement>>(browserMeasurementsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        foreach (var sample in samples)
        {
            var browserMeasurement = browserMeasurements.Single(measurement => measurement.Id == sample.Id);
            var delta = Math.Abs(browserMeasurement.Width - sample.ExpectedWidth);
            var tolerance = Math.Max(0.01, sample.ExpectedWidth * 0.005);
            Assert.IsTrue(
                delta <= tolerance,
                $"{sample.Id}: C# width {sample.ExpectedWidth:0.###} must match browser {browserMeasurement.Width:0.###} within 0.5 %. Delta {delta:0.###}, tolerance {tolerance:0.###}. Browser font: {browserMeasurement.Font}.");
        }

        var canvasMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.GetByTestId("reporting-canvas"));
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);

        var screenshotDirectory = CreateScreenshotDirectory();
        var screenshotPath = Path.Combine(screenshotDirectory, "01-fidelity-sample.png");
        await page.GetByTestId("reporting-harness-page").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new
                {
                    phase = "F0",
                    testName = nameof(F0_ReportingHarness_MatchesBrowserMeasureTextAndCapturesFidelityScreenshot),
                    screenshotPath,
                    functionalReview = "Text runs are rendered at the C# supplied x/y/width values; browser measurements stay within the 0.5 % fidelity gate; no text rectangles overlap.",
                    uxReview = "The sample page uses a quiet print-sheet composition, stable baselines, readable line spacing, and unobtrusive measurement guides.",
                    canvasMetrics,
                    measurements = samples.Select(sample => new
                    {
                        sample.Id,
                        sample.Text,
                        sample.FontFamily,
                        sample.FontSize,
                        sample.Bold,
                        sample.LetterSpacing,
                        csharpWidth = sample.ExpectedWidth,
                        browserWidth = browserMeasurements.Single(measurement => measurement.Id == sample.Id).Width
                    })
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(screenshotPath);
        TestContext.AddResultFile(manifestPath);
        await browserContext.CloseAsync();
    }

    private static F0FontSet LoadF0Fonts()
    {
        var sansPath = FirstExistingPath(
            "/usr/share/fonts/truetype/freefont/FreeSans.ttf",
            "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf");
        var sansBoldPath = FirstExistingPath(
            "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf",
            "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationMono-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf");
        var cjkPath = FirstExistingPath(
            "/usr/share/fonts/truetype/droid/DroidSansFallbackFull.ttf",
            "/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf");

        if (sansPath is null || sansBoldPath is null || cjkPath is null)
        {
            Assert.Inconclusive("F0 reporting fidelity test requires local TrueType sans, bold, and CJK fonts.");
        }

        var sansBytes = File.ReadAllBytes(sansPath!);
        var sansBoldBytes = File.ReadAllBytes(sansBoldPath!);
        var cjkBytes = File.ReadAllBytes(cjkPath!);
        return new F0FontSet(
            sansPath!,
            sansBoldPath!,
            cjkPath!,
            sansBytes,
            sansBoldBytes,
            cjkBytes,
            ReadFace(sansBytes, "Tempo F0 Sans", FontStyleKey.Regular),
            ReadFace(sansBoldBytes, "Tempo F0 Sans", FontStyleKey.Bold),
            ReadFace(cjkBytes, "Tempo F0 CJK", FontStyleKey.Regular));
    }

    private static FontMetricFace ReadFace(byte[] bytes, string familyName, FontStyleKey styleKey)
    {
        using var stream = new MemoryStream(bytes);
        return TrueTypeFontMetricReader.Read(stream, familyName, styleKey);
    }

    private static string? FirstExistingPath(params string[] paths)
        => paths.FirstOrDefault(File.Exists);

    private static FontMetricFace ApplyHintedCalibration(FontMetricFace face, IEnumerable<FontHintCalibration> calibration)
    {
        var style = face.StyleKey.Italic ? "italic" : "normal";
        var weight = face.StyleKey.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hintedAdvances = calibration
            .Where(item =>
                string.Equals(item.Family, face.FamilyName, StringComparison.Ordinal)
                && string.Equals(item.Weight, weight, StringComparison.Ordinal)
                && string.Equals(item.Style, style, StringComparison.Ordinal))
            .GroupBy(static item => item.PixelsPerEm)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyDictionary<int, ushort>)group.ToDictionary(
                    static item => item.CodePoint,
                    static item => (ushort)Math.Clamp(item.Width, 0, ushort.MaxValue)));

        return new FontMetricFace(
            face.FamilyName,
            face.StyleKey,
            face.UnitsPerEm,
            face.Ascent,
            face.Descent,
            face.LineGap,
            face.MissingGlyphAdvanceWidth,
            face.AdvanceWidths,
            face.KerningPairs,
            hintedAdvances,
            face.MissingGlyphHintedAdvanceWidths);
    }

    private static IReadOnlyList<BrowserMeasureRequest> BuildSampleRequests()
        =>
        [
            new BrowserMeasureRequest
            {
                Id = "latin-regular",
                Text = "Tempo Reporting 012345",
                FontFamily = "Tempo F0 Sans",
                FontSize = 18,
                FontWeight = "400",
                FontStyle = "normal",
                Kerning = false
            },
            new BrowserMeasureRequest
            {
                Id = "czech-diacritics",
                Text = "Žluťoučký kůň ěščřžýáíé",
                FontFamily = "Tempo F0 Sans",
                FontSize = 16,
                FontWeight = "400",
                FontStyle = "normal",
                LetterSpacing = 0.2,
                Kerning = false
            },
            new BrowserMeasureRequest
            {
                Id = "bold-run",
                Text = "Bold metrics 123",
                FontFamily = "Tempo F0 Sans",
                FontSize = 22,
                FontWeight = "700",
                FontStyle = "normal",
                Kerning = false
            },
            new BrowserMeasureRequest
            {
                Id = "greek-cyrillic",
                Text = "Ωmega Москва",
                FontFamily = "Tempo F0 Sans",
                FontSize = 17,
                FontWeight = "400",
                FontStyle = "normal",
                Kerning = false
            },
            new BrowserMeasureRequest
            {
                Id = "cjk",
                Text = "会社報告",
                FontFamily = "Tempo F0 CJK",
                FontSize = 20,
                FontWeight = "400",
                FontStyle = "normal",
                Kerning = false
            }
        ];

    private static List<FidelitySample> BuildSamples(IEnumerable<BrowserMeasureRequest> requests, ITextMeasurer measurer)
    {
        return requests.Select(request =>
        {
            var measurement = measurer.MeasureRun(new TextMeasureRequest(
                request.Text,
                request.FontFamily,
                request.FontSize,
                Bold: request.FontWeight == "700",
                Italic: request.FontStyle == "italic",
                LetterSpacing: request.LetterSpacing,
                Kerning: request.Kerning));
            return new FidelitySample(
                request.Id,
                request.Text,
                request.FontFamily,
                request.FontSize,
                request.FontWeight == "700",
                request.FontStyle == "italic",
                request.LetterSpacing,
                measurement.Width,
                measurement.LineHeight);
        }).ToList();
    }

    private static ReportSnapshot BuildSnapshot(IReadOnlyList<FidelitySample> samples)
    {
        var commands = new List<ReportSnapshotCommand>
        {
            ReportSnapshotCommand.Rectangle("page", 0, 0, 794, 520, "#ffffff", "#d1d5db", 1),
            ReportSnapshotCommand.Rectangle("header-band", 48, 36, 698, 36, "#f8fafc", "#e5e7eb", 1)
        };

        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var baseline = 92 + index * 72;
            commands.Add(ReportSnapshotCommand.Line($"guide-{sample.Id}", 72, baseline + 10, 640, 0, "#e5e7eb", 1));
            commands.Add(ReportSnapshotCommand.TextRun(
                sample.Id,
                sample.Text,
                72,
                baseline,
                sample.ExpectedWidth,
                Math.Max(sample.LineHeight, sample.FontSize * 1.25),
                sample.FontFamily,
                sample.FontSize,
                "#111827",
                sample.Bold ? "700" : "400",
                sample.Italic ? "italic" : "normal",
                sample.LetterSpacing));
        }

        return new ReportSnapshot
        {
            SnapshotId = "reporting-f0-fidelity",
            Pages =
            [
                new ReportSnapshotPage
                {
                    PageNumber = 1,
                    Width = 794,
                    Height = 520,
                    Commands = commands
                }
            ]
        };
    }

    private static string CreateScreenshotDirectory()
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "reporting",
            "f0");
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed record FidelitySample(
        string Id,
        string Text,
        string FontFamily,
        double FontSize,
        bool Bold,
        bool Italic,
        double LetterSpacing,
        double ExpectedWidth,
        double LineHeight);

    private sealed record F0FontSet(
        string SansPath,
        string SansBoldPath,
        string CjkPath,
        byte[] SansBytes,
        byte[] SansBoldBytes,
        byte[] CjkBytes,
        FontMetricFace SansFace,
        FontMetricFace SansBoldFace,
        FontMetricFace CjkFace)
    {
        public IReadOnlyDictionary<string, string> ToStaticFiles()
            => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sans.ttf"] = SansPath,
                ["sans-bold.ttf"] = SansBoldPath,
                ["cjk.ttf"] = CjkPath
            };

        public IReadOnlyList<FontFacePayload> ToFontFaces(string baseUrl)
            =>
            [
                new()
                {
                    Family = "Tempo F0 Sans",
                    Weight = "400",
                    Style = "normal",
                    Format = "truetype",
                    Url = $"{baseUrl}/__f0fonts/sans.ttf"
                },
                new()
                {
                    Family = "Tempo F0 Sans",
                    Weight = "700",
                    Style = "normal",
                    Format = "truetype",
                    Url = $"{baseUrl}/__f0fonts/sans-bold.ttf"
                },
                new()
                {
                    Family = "Tempo F0 CJK",
                    Weight = "400",
                    Style = "normal",
                    Format = "truetype",
                    Url = $"{baseUrl}/__f0fonts/cjk.ttf"
                }
            ];
    }

    private sealed class FontFacePayload
    {
        public string Family { get; set; } = string.Empty;

        public string Weight { get; set; } = "400";

        public string Style { get; set; } = "normal";

        public string Format { get; set; } = "truetype";

        public string Base64 { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
    }

    private sealed class FontFetchProbe
    {
        public string Family { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public bool Ok { get; set; }

        public int Status { get; set; }

        public int ByteLength { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    private sealed class FontHintCalibration
    {
        public string Family { get; set; } = string.Empty;

        public string Weight { get; set; } = "400";

        public string Style { get; set; } = "normal";

        public int PixelsPerEm { get; set; }

        public int CodePoint { get; set; }

        public int Width { get; set; }
    }

    private sealed class BrowserMeasureRequest
    {
        public string Id { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public string FontFamily { get; set; } = string.Empty;

        public double FontSize { get; set; }

        public string FontWeight { get; set; } = "400";

        public string FontStyle { get; set; } = "normal";

        public double LetterSpacing { get; set; }

        public bool Kerning { get; set; }
    }

    private sealed class BrowserMeasurement
    {
        public string Id { get; set; } = string.Empty;

        public double Width { get; set; }

        public double NaturalWidth { get; set; }

        public string Font { get; set; } = string.Empty;
    }

    private sealed class ReportingHarnessSummary
    {
        public int CommandCount { get; set; }

        public int PaintedCommandCount { get; set; }

        public int TextRunCount { get; set; }

        public double PixelRatio { get; set; }
    }

    private sealed class ReportingHarnessLoadResult
    {
        public bool Ok { get; set; }

        public ReportingHarnessSummary? Summary { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    private sealed class ReportingStaticHost : IAsyncDisposable
    {
        private readonly DirectoryInfo _repositoryRoot;
        private readonly IReadOnlyDictionary<string, string> _extraFiles;
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _serverTask;

        private ReportingStaticHost(DirectoryInfo repositoryRoot, IReadOnlyDictionary<string, string> extraFiles, int port)
        {
            _repositoryRoot = repositoryRoot;
            _extraFiles = extraFiles;
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{BaseUrl}/");
            _listener.Start();
            _serverTask = Task.Run(() => ServeAsync(_cancellation.Token));
        }

        public string BaseUrl { get; }

        public static ReportingStaticHost Start(DirectoryInfo repositoryRoot, IReadOnlyDictionary<string, string> extraFiles)
            => new(repositoryRoot, extraFiles, GetFreePort());

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                await _serverTask;
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _listener.Close();
                _cancellation.Dispose();
            }
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => ServeRequestAsync(context, cancellationToken), cancellationToken);
            }
        }

        private async Task ServeRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var path = ResolvePath(context.Request.Url?.AbsolutePath ?? "/");
                if (path is null || !File.Exists(path))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                context.Response.ContentType = ContentTypeFor(path);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            }
            catch
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500;
                }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private string? ResolvePath(string requestPath)
        {
            var decoded = WebUtility.UrlDecode(requestPath).Replace('\\', '/');
            if (decoded == "/")
            {
                decoded = "/reporting-harness.html";
            }

            if (decoded.StartsWith("/_content/Tempo.Blazor/", StringComparison.Ordinal))
            {
                var relative = decoded["/_content/Tempo.Blazor/".Length..];
                return SafeCombine(Path.Combine(_repositoryRoot.FullName, "src", "Tempo.Blazor", "wwwroot"), relative);
            }

            if (decoded.StartsWith("/__f0fonts/", StringComparison.Ordinal))
            {
                var key = decoded["/__f0fonts/".Length..];
                return _extraFiles.TryGetValue(key, out var path) ? path : null;
            }

            return SafeCombine(Path.Combine(_repositoryRoot.FullName, "src", "Tempo.Blazor.Demo", "wwwroot"), decoded.TrimStart('/'));
        }

        private static string? SafeCombine(string root, string relativePath)
        {
            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            return fullPath.StartsWith(fullRoot, StringComparison.Ordinal) ? fullPath : null;
        }

        private static string ContentTypeFor(string path)
            => Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".css" => "text/css; charset=utf-8",
                ".html" => "text/html; charset=utf-8",
                ".js" or ".mjs" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                ".ttf" => "font/ttf",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                _ => "application/octet-stream"
            };

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
