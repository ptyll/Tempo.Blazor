using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Icons;

/// <summary>
/// TDD tests for IconRegistry and IIconProvider.
/// RED phase: written BEFORE implementation — must FAIL until IconRegistry is implemented.
/// </summary>
public class TmIconRegistryTests : LocalizationTestBase, IDisposable
{
    // Reset registry state between tests (important for static state)
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            IconRegistry.Reset();
        base.Dispose(disposing);
    }

    // ─── IconRegistry.Register ────────────────────────────────────────────────

    [Fact]
    public void IconRegistry_Register_CustomIcon_Renders_InTmIcon()
    {
        // RED: Register custom SVG, then render TmIcon — should use it
        const string customSvg = "<circle cx=\"12\" cy=\"12\" r=\"10\"/>";
        IconRegistry.Register("my-custom-icon", customSvg);

        var cut = RenderComponent<TmIcon>(p => p.Add(c => c.Name, "my-custom-icon"));

        cut.Markup.Should().Contain("circle");
    }

    [Fact]
    public void IconRegistry_Register_Overrides_Existing_Name()
    {
        // RED: Registering same name twice uses the latest value
        IconRegistry.Register("test-icon", "<circle/>");
        IconRegistry.Register("test-icon", "<rect width=\"10\" height=\"10\"/>");

        var svg = IconRegistry.Resolve("test-icon");

        svg.Should().Contain("rect");
        svg.Should().NotContain("circle");
    }

    [Fact]
    public void IconRegistry_Unknown_Icon_Renders_Fallback_Span()
    {
        // RED: Icon not found anywhere → render <span class="tm-icon-unknown">
        var cut = RenderComponent<TmIcon>(p => p.Add(c => c.Name, "completely-unknown-xyz-icon"));

        cut.Find("span.tm-icon-unknown").Should().NotBeNull();
    }

    [Fact]
    public void IconRegistry_Custom_Provider_Called_For_Unknown_BuiltIn_Name()
    {
        // RED: Custom provider is consulted when built-in switch doesn't match
        var provider = new TestIconProvider("provider-icon", "<line x1=\"0\" y1=\"0\" x2=\"24\" y2=\"24\"/>");
        IconRegistry.RegisterProvider(provider);

        var cut = RenderComponent<TmIcon>(p => p.Add(c => c.Name, "provider-icon"));

        cut.Markup.Should().Contain("line");
        provider.WasCalled.Should().BeTrue();
    }

    [Fact]
    public void IconRegistry_Case_Insensitive_Lookup()
    {
        // RED: Registry lookup is case-insensitive
        IconRegistry.Register("MyIcon", "<path d=\"M1 2 3\"/>");

        var lowerResult = IconRegistry.Resolve("myicon");
        var upperResult = IconRegistry.Resolve("MYICON");
        var mixedResult = IconRegistry.Resolve("MyIcon");

        lowerResult.Should().NotBeNull();
        upperResult.Should().NotBeNull();
        mixedResult.Should().NotBeNull();
    }

    [Fact]
    public void IconRegistry_Contains_ReturnsFalse_ForUnregisteredIcon()
    {
        var result = IconRegistry.Contains("not-registered-at-all");

        result.Should().BeFalse();
    }

    [Fact]
    public void IconRegistry_Contains_ReturnsTrue_AfterRegister()
    {
        IconRegistry.Register("registered-icon", "<path/>");

        IconRegistry.Contains("registered-icon").Should().BeTrue();
    }

    [Fact]
    public void IconRegistry_Contains_ReturnsTrue_WhenProviderHasIcon()
    {
        var provider = new TestIconProvider("provider-has-icon", "<path/>");
        IconRegistry.RegisterProvider(provider);

        IconRegistry.Contains("provider-has-icon").Should().BeTrue();
    }

    [Fact]
    public void IconRegistry_Resolve_ReturnsNull_WhenNotRegistered()
    {
        var result = IconRegistry.Resolve("does-not-exist-anywhere");

        result.Should().BeNull();
    }

    [Fact]
    public void IconRegistry_RegisteredIcon_TakePrecedence_OverProvider()
    {
        // Direct registered icons beat providers
        const string directSvg = "<rect id=\"direct\"/>";
        IconRegistry.Register("overlap-icon", directSvg);
        IconRegistry.RegisterProvider(new TestIconProvider("overlap-icon", "<circle id=\"provider\"/>"));

        var result = IconRegistry.Resolve("overlap-icon");

        result.Should().Contain("direct");
        result.Should().NotContain("provider");
    }

    // ─── Helper: Test IIconProvider implementation ────────────────────────────

    private sealed class TestIconProvider : IIconProvider
    {
        private readonly string _iconName;
        private readonly string _svg;

        public bool WasCalled { get; private set; }

        public TestIconProvider(string iconName, string svg)
        {
            _iconName = iconName;
            _svg = svg;
        }

        public bool HasIcon(string iconName)
        {
            WasCalled = true;
            return string.Equals(iconName, _iconName, StringComparison.OrdinalIgnoreCase);
        }

        public string? GetSvg(string iconName) =>
            string.Equals(iconName, _iconName, StringComparison.OrdinalIgnoreCase) ? _svg : null;
    }
}
