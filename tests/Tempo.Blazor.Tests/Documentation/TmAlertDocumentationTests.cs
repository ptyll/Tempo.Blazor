using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Guards the documentation of <c>TmAlert.Role</c> (plan 8fa7319c phase 4, release regression check).
/// <para>
/// The parameter only matters to a consumer who is TOLD about it: a host that never learns
/// <c>Role="status"</c> exists keeps shipping success banners as a second assertive alert. The
/// parameter is documented in the editable overlay and in every committed bundle an MCP client or
/// the package docs actually read, and the polite success example travels with it.
/// </para>
/// </summary>
public class TmAlertDocumentationTests
{
    private const string OverlayPath = "JsonDocumentation/Components/TmAlert.json";

    private static JsonElement LoadRepoJson(string relativePath)
    {
        var path = Path.Combine(ThemeCss.RepositoryRoot().FullName, relativePath);
        File.Exists(path).Should().BeTrue($"{relativePath} is committed at the repo root");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static JsonElement AlertItem(string bundlePath)
    {
        var document = LoadRepoJson(bundlePath);
        var items = document.TryGetProperty("items", out var flat)
            ? flat.EnumerateArray().ToList()
            : document.GetProperty("packages").EnumerateArray()
                .SelectMany(package => package.GetProperty("items").EnumerateArray())
                .ToList();

        return items.Single(item => item.GetProperty("itemName").GetString() == "TmAlert");
    }

    private static JsonElement RoleParameter(JsonElement item)
        => item.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "Role");

    private static List<string> Examples(JsonElement item)
        => item.GetProperty("examples").EnumerateArray()
            .Select(example => example.GetProperty("code").GetString() ?? string.Empty)
            .ToList();

    [Fact]
    public void Overlay_Documents_Role_As_An_OptIn_Root_Role()
    {
        var role = RoleParameter(LoadRepoJson(OverlayPath));

        role.GetProperty("type").GetString().Should().Be("string");
        role.GetProperty("default").GetString().Should().Be("alert",
            "the shipped default must stay the assertive live region");
        role.GetProperty("description").GetString().Should()
            .Contain("status", "the overlay names the polite value a host can opt into");
    }

    [Theory]
    [InlineData("tempo-blazor.json")]
    [InlineData("tempo-blazor-documentation.json")]
    [InlineData("tempo-blazor-all.json")]
    public void Bundle_Documents_Role_With_The_Polite_Success_Example(string bundle)
    {
        var item = AlertItem(bundle);
        var role = RoleParameter(item);

        // The generator renders a string default quoted (\"alert\"); the overlay keeps the bare word.
        role.GetProperty("default").GetString()!.Trim('"').Should().Be("alert",
            "{0} must ship the same default the component has", bundle);
        role.GetProperty("description").GetString().Should().Contain("status");

        var examples = Examples(item);
        examples.Should().Contain(code =>
                code.Contains("Severity=\"AlertSeverity.Success\"", StringComparison.Ordinal)
                && code.Contains("Role=\"status\"", StringComparison.Ordinal),
            "{0} must show the success banner announced politely — the reason TmAlert.Role exists", bundle);
    }

    [Theory]
    [InlineData("tempo-blazor.json")]
    [InlineData("tempo-blazor-documentation.json")]
    [InlineData("tempo-blazor-all.json")]
    public void Polite_Success_Example_Is_Documented_Exactly_Once(string bundle)
    {
        Examples(AlertItem(bundle))
            .Where(code => code.Contains("Role=\"status\"", StringComparison.Ordinal))
            .Should().ContainSingle("{0} carries the polite-success example once", bundle);
    }
}
