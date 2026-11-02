using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Validation half of <see cref="TmStatCard"/>: <see cref="TmStatCard.SubValueColor"/> is written
/// into an inline <c>style</c>, so before 2.9.0 any string reached the attribute —
/// <c>text-green-600</c> produced a dead declaration (the silent colour loss the changelog now
/// owns) and <c>red; background:url(…)</c> injected extra declarations. Since 2.9.0 the value
/// must BE a CSS colour: a <c>var(--…)</c> reference, a hex literal, an
/// <c>rgb/hsl/oklch/color-mix</c> function, or a named colour. Anything else emits no
/// <c>style</c> and logs a warning through the optionally injected <see cref="ILoggerFactory"/>.
/// The grammar itself lives in <see cref="CssColorValue"/> because a breakdown part's colour needs
/// exactly the same validation.
/// </summary>
public partial class TmStatCard
{
    /// <summary>
    /// Optional logging: resolved through <see cref="IServiceProvider"/> so a consumer without
    /// logging registered simply gets no warning — never a missing-service exception.
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private string? _validatedSubValueColor;
    private ILogger? _logger;
    private bool _loggerResolved;

    /// <summary>
    /// Returns <see cref="SubValueColor"/> when it is a CSS colour, <see langword="null"/> after
    /// logging a warning otherwise. Called from <see cref="OnParametersSet"/> so a value turned
    /// invalid on re-render drops the style on that render, not the next.
    /// </summary>
    private string? ValidateSubValueColor()
    {
        if (string.IsNullOrEmpty(SubValueColor) || IsValidCssColor(SubValueColor))
        {
            return SubValueColor;
        }

        if (!_loggerResolved)
        {
            _loggerResolved = true;
            _logger = (Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)
                ?.CreateLogger<TmStatCard>();
        }

        _logger?.LogWarning(
            "TmStatCard: SubValueColor=\"{Value}\" is not a CSS color — no style emitted " +
            "(Title=\"{Title}\"). SubValueColor expects a CSS color (2.9.0 breaking change: " +
            "CSS class names are no longer accepted).",
            SubValueColor,
            Title);
        return null;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a CSS <c>&lt;color&gt;</c> the component may write into
    /// the inline style. The grammar is deliberately narrower than the full CSS syntax; see
    /// <see cref="CssColorValue.IsValid"/> for the exact rules.
    /// </summary>
    /// <param name="value">The candidate colour value.</param>
    internal static bool IsValidCssColor(string value) => CssColorValue.IsValid(value);
}
