using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>TDD tests for TmStatCard.</summary>
public class TmStatCardTests : LocalizationTestBase
{
    [Fact]
    public void TmStatCard_Has_Base_CssClass()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.Find(".tm-stat-card").Should().NotBeNull();
    }

    [Fact]
    public void TmStatCard_Renders_Value()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.Find(".tm-stat-value").TextContent.Should().Contain("1,234");
    }

    [Fact]
    public void TmStatCard_Renders_Title()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Active users")
            .Add(c => c.Value, "42"));

        cut.Find(".tm-stat-label").TextContent.Should().Contain("Active users");
    }

    [Fact]
    public void TmStatCard_Renders_SubValue_When_Set()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12% this month"));

        cut.Find(".tm-stat-subvalue").TextContent.Should().Contain("+12% this month");
    }

    [Fact]
    public void TmStatCard_No_SubValue_When_Null()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000"));

        cut.FindAll(".tm-stat-subvalue").Should().BeEmpty();
    }

    /// <summary>
    /// <c>SubValueColor</c> without <c>SubValue</c> is an affordance without a mechanism: the call site
    /// states an intent that rendering silently denies, because the whole span hangs off
    /// <c>SubValue</c>. The card refuses it instead of ignoring it.
    /// </summary>
    /// <remarks>
    /// The invariant lives in the COMPONENT, not in a markup scanner, and that placement is the point: it
    /// covers splatted <c>@attributes</c>, <c>DynamicComponent</c> and consumers outside this repository,
    /// and it fails at the moment of misuse. A scanner is a legitimate second line for the release gate,
    /// but it cannot see those paths and must carry its own denominator.
    /// </remarks>
    [Fact]
    public void TmStatCard_Rejects_SubValueColor_Without_SubValue()
    {
        var act = () => Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValueColor, "tm-text-success"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SubValueColor*")
            .WithMessage("*SubValue*")
            .WithMessage("*Revenue*", "the message has to say WHICH card, or it is a riddle in a log");
    }

    /// <summary>The invariant is about the PAIR, so neither half alone may be made illegal.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("+12%", null)]
    [InlineData("+12%", "tm-text-success")]
    public void TmStatCard_Accepts_EverySoundCombination(string? subValue, string? subValueColor)
    {
        var act = () => Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, subValue)
            .Add(c => c.SubValueColor, subValueColor));

        act.Should().NotThrow();
    }

    /// <summary>
    /// The rejection must survive a LATER parameter change too — a card that starts sound and is then
    /// re-rendered with the value removed is the same broken state, reached one render later.
    /// </summary>
    [Fact]
    public void TmStatCard_Rejects_SubValue_Removed_On_Rerender()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "tm-text-success"));

        var act = () => cut.Render(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, (string?)null)
            .Add(c => c.SubValueColor, "tm-text-success"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SubValueColor*");
    }

    /// <summary>
    /// ed256446: <c>SubValueColor</c> must WIN over the base <c>.tm-stat-subvalue</c> colour, resolved
    /// the way a browser resolves it — through the shipped bundle, not by hoping a consumer rule lands
    /// later in source order. The base declaration names the custom property with the secondary token
    /// as fallback, and the component wires the parameter into that property on the element itself.
    /// </summary>
    /// <remarks>
    /// Mutation: reverting the declaration to <c>color: var(--tm-text-secondary)</c> makes the winner
    /// the plain token and this test goes red, because the painted colour then equals the base.
    /// </remarks>
    [Fact]
    public void TmStatCard_SubValueColor_Wins_Over_The_Base_Rule()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "var(--tm-color-success-text)"));

        var span = cut.Find(".tm-stat-subvalue");
        (span.GetAttribute("style") ?? string.Empty).Should().Contain(
            "--tm-stat-subvalue-color: var(--tm-color-success-text)",
            "the parameter has to reach the element's own custom property — a class cannot promise "
            + "a colour wins, because specificity and source order decide that elsewhere");

        var winner = CssCascade.Winning(
            ThemeCss.BundledCss(),
            [new CssCascade.Element("div", "tm-stat-card"), new CssCascade.Element("span", "tm-stat-subvalue")],
            "color");

        var tokens = ThemeCss.TokenGraph(dark: false);
        tokens["--tm-stat-subvalue-color"] = "var(--tm-color-success-text)";

        var painted = ThemeCss.ResolveColour(winner, tokens);
        painted.Should().Be(
            ThemeCss.ResolveColour("var(--tm-color-success-text)", tokens),
            "SubValueColor vítězí nad základem — element nese vlastní hodnotu custom property, "
            + "takže fallback na --tm-text-secondary se neuplatní");
        painted.Should().NotBe(
            ThemeCss.ResolveColour("var(--tm-text-secondary)", tokens),
            "jinak parametr maluje stejnou barvou jako základ — přesně vada ed256446");
    }

    /// <summary>Without <c>SubValueColor</c> the span carries no stray <c>style</c> attribute.</summary>
    [Fact]
    public void TmStatCard_No_Style_Attribute_Without_SubValueColor()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%"));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().BeNull();
    }

    /// <summary>
    /// 2.9.0 breaking change made honest: <c>SubValueColor</c> is interpolated verbatim into an
    /// inline <c>style</c>, so the component must VALIDATE it as a CSS colour before emitting.
    /// Every grammar the contract names reaches the attribute — token reference, every hex length,
    /// the rgb/hsl/oklch/color-mix functions, and the named-colour keywords.
    /// </summary>
    [Theory]
    [InlineData("var(--tm-color-success-text)")]
    [InlineData("var(--x, green)")]
    [InlineData("#fff")]
    [InlineData("#ffff")]
    [InlineData("#16a34a")]
    [InlineData("#16a34a80")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("rgb(1 2 3 / .5)")]
    [InlineData("rgba(1,2,3,.5)")]
    [InlineData("hsl(120,50%,50%)")]
    [InlineData("hsla(120,50%,50%,.5)")]
    [InlineData("oklch(0.7 0.15 150)")]
    [InlineData("oklch(from var(--x) l c h)")]
    [InlineData("color-mix(in srgb, red 50%, blue)")]
    [InlineData("green")]
    [InlineData("rebeccapurple")]
    [InlineData("currentcolor")]
    [InlineData("transparent")]
    public void TmStatCard_SubValueColor_ValidCssColor_ReachesTheStyleAttribute(string color)
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, color));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().Be(
            $"--tm-stat-subvalue-color: {color}",
            "platná CSS barva se propíše do custom property beze změny");
    }

    /// <summary>
    /// The other half of the contract: a value that is not a CSS colour emits NO style and logs a
    /// warning — the old class-name usage (<c>text-green-600</c>) is exactly this case, and an
    /// injected payload (<c>; background:url(…)</c>) must never reach the attribute. No throw:
    /// the caller gets a warning and an uncoloured sub-value, not a crashed render.
    /// </summary>
    [Theory]
    [InlineData("text-green-600")]
    [InlineData("tm-text-success")]
    [InlineData("not-a-color")]
    [InlineData("red; background:url(x)")]
    [InlineData("red; --tm-stat-subvalue-color: blue")]
    [InlineData("url(//evil.example/x)")]
    [InlineData("rgb(1,2,3};x{y")]
    [InlineData("rgb(1,2,3")]
    [InlineData("var(")]
    public void TmStatCard_SubValueColor_InvalidValue_EmitsNoStyleAndLogsWarning(string color)
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, color));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().BeNull(
            "neplatná hodnota nesmí dosáhnout atributu — jinak je injektovaná deklarace zpět ve hře");
        factory.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Warning
                     && entry.Message.Contains("SubValueColor expects a CSS color")
                     && entry.Message.Contains("CSS class names are no longer accepted"),
            "neplatná hodnota musí zalogovat warning s textem breaking-change kontraktu, " +
            $"zaznamenáno: {string.Join(" | ", factory.Entries.Select(e => e.Message))}");
    }

    /// <summary>
    /// The warning must fire again when a VALID colour is swapped for an invalid one on re-render —
    /// the check lives in <c>OnParametersSet</c>, not in a first-render branch.
    /// </summary>
    [Fact]
    public void TmStatCard_SubValueColor_TurnedInvalidOnRerender_DropsTheStyle()
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "var(--tm-color-success-text)"));

        cut.Render(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "text-green-600"));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().BeNull();
        factory.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    /// <summary>A logger the test owns: records (level, formatted message) so the assertion reads intent, not NSubstitute call-shape.</summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
