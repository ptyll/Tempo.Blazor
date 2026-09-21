using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Tempo.Blazor.E2E.Mcp;

namespace Tempo.Blazor.E2E;

/// <summary>F18 reporting MCP AI-friendly create/validate/preview smoke gate.</summary>
[TestClass]
[TestCategory("Reporting")]
[TestCategory("Reporting:F18")]
[DoNotParallelize]
public sealed class ReportingF18McpE2ETests
{
    [TestMethod]
    public async Task F18_McpReporting_CreateValidatePreviewFlow()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var apiHost = await DotnetWebAppHost.StartAsync(
            repositoryRoot,
            "Demo API",
            Path.Combine("src", "Tempo.Blazor.Demo.Api", "Tempo.Blazor.Demo.Api.csproj")).ConfigureAwait(false);
        using var http = new HttpClient();
        var client = new McpJsonRpcClient(http, new Uri($"{apiHost.BaseUrl}/mcp"));
        await client.InitializeAsync().ConfigureAwait(false);

        var tools = await client.ListToolsAsync().ConfigureAwait(false);
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "list_reports",
                "get_report_definition",
                "create_report",
                "update_report_elements",
                "validate_report",
                "render_report_preview",
            },
            toolNames.ToList());

        var created = await client.CallToolAsync("create_report", new { name = "F18 MCP Smoke" }).ConfigureAwait(false);
        Assert.IsTrue(created.GetProperty("success").GetBoolean(), created.GetRawText());
        var reportId = created.GetProperty("reportId").GetString();

        var operationsJson = JsonSerializer.Serialize(new object[]
        {
            new { op = "setBandHeight", band = "detail", height = 72 },
            new
            {
                op = "addElement",
                band = "detail",
                element = new
                {
                    type = "textBox",
                    id = "f18-label",
                    x = 0,
                    y = 0,
                    width = 240,
                    height = 24,
                    text = "MCP preview ready"
                }
            }
        });
        var updated = await client.CallToolAsync("update_report_elements", new { reportId, operationsJson }).ConfigureAwait(false);
        Assert.IsTrue(updated.GetProperty("success").GetBoolean(), updated.GetRawText());

        var loaded = await client.CallToolAsync("get_report_definition", new { reportId }).ConfigureAwait(false);
        var definitionJson = loaded.GetProperty("definition").GetRawText();
        var validation = await client.CallToolAsync("validate_report", new { definitionJson, includeSchema = true }).ConfigureAwait(false);
        Assert.IsTrue(validation.GetProperty("valid").GetBoolean(), validation.GetRawText());
        Assert.IsTrue(validation.GetProperty("schema").GetProperty("elementTypes").GetArrayLength() >= 7);

        var preview = await client.CallToolAsync("render_report_preview", new { reportId }).ConfigureAwait(false);
        Assert.IsTrue(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.AreEqual("image/png", preview.GetProperty("contentType").GetString());
        var png = Convert.FromBase64String(preview.GetProperty("base64").GetString()!);
        CollectionAssert.AreEqual(
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A },
            png.Take(8).ToArray());
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class DotnetWebAppHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly List<string> _log = [];
        private readonly string _name;

        private DotnetWebAppHost(string name, Process process, int port)
        {
            _name = name;
            _process = process;
            BaseUrl = $"http://127.0.0.1:{port}";
        }

        public string BaseUrl { get; }

        public static async Task<DotnetWebAppHost> StartAsync(
            DirectoryInfo repositoryRoot,
            string name,
            string projectRelativePath)
        {
            var port = GetFreePort();
            var projectPath = Path.Combine(repositoryRoot.FullName, projectRelativePath);
            var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{projectPath}\" --urls http://127.0.0.1:{port}")
            {
                WorkingDirectory = repositoryRoot.FullName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
            // Api:BaseUrl stays empty in the shipped appsettings → the portal runs self-contained:
            // ITempoReportServerClient resolves to the in-memory DemoTempoReportServerClient, so no
            // external Report Server Api is needed for catalog/favorites/schedules pages.
            startInfo.Environment["ReportServer__BaseUrl"] = $"http://127.0.0.1:{port}";
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start dotnet run for {name}.");
            var host = new DotnetWebAppHost(name, process, port);
            process.OutputDataReceived += (_, args) => host.AddLog(args.Data);
            process.ErrorDataReceived += (_, args) => host.AddLog(args.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await host.WaitUntilReadyAsync().ConfigureAwait(false);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }

            _process.Dispose();
        }

        private void AddLog(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lock (_log)
                {
                    _log.Add(line);
                }
            }
        }

        private async Task WaitUntilReadyAsync()
        {
            using var client = new HttpClient();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"{_name} exited early: {string.Join(Environment.NewLine, _log)}");
                }

                try
                {
                    using var response = await client.GetAsync(BaseUrl).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Redirect)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(300).ConfigureAwait(false);
            }

            throw new TimeoutException($"{_name} did not become ready: {string.Join(Environment.NewLine, _log)}");
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
