using System.Text;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The one place an <see cref="AgentGraphNodeKind"/> becomes an icon, a stable CSS tone slug and a localized WORD, plus
/// the pure label wrapping a node box needs.
/// <para>
/// Deliberately internal: this is presentation, not a contract (the contract is the enum's meaning). It exists so the
/// graph, its table alternative and the node avatar cannot drift — one vocabulary everywhere.
/// </para>
/// <para>
/// An unknown value (an enum member from a newer backend, or a cast integer) falls back to the neutral glyph and word,
/// so nothing throws; <see cref="IsKnownKind"/> lets the component refuse to paint a value it cannot name.
/// </para>
/// </summary>
internal static class AgentGraphPresentation
{
    /// <summary>The built-in icon id of a node kind. Painted in the avatar of a node that is not an agent.</summary>
    /// <param name="kind">The kind to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant; <see cref="IconNames.Circle"/> for an unknown value.</returns>
    public static string KindIcon(AgentGraphNodeKind kind) => kind switch
    {
        AgentGraphNodeKind.Tool => IconNames.Terminal,
        AgentGraphNodeKind.Resource => IconNames.Database,
        AgentGraphNodeKind.Human => IconNames.User,
        AgentGraphNodeKind.Agent => IconNames.Users,
        _ => IconNames.Circle,
    };

    /// <summary>The stable CSS modifier slug of a node kind.</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>A lowercase slug, spelled out rather than derived from <c>ToString().ToLower()</c>.</returns>
    public static string KindSlug(AgentGraphNodeKind kind) => kind switch
    {
        AgentGraphNodeKind.Agent => "agent",
        AgentGraphNodeKind.Tool => "tool",
        AgentGraphNodeKind.Resource => "resource",
        AgentGraphNodeKind.Human => "human",
        _ => "other",
    };

    /// <summary>The localized word of a node kind, in the supplied localizer's language.</summary>
    /// <param name="localizer">The localizer to read the shared <c>TmAgentGraph_Kind_*</c> keys from.</param>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The translated word; the neutral "Agent" word for an unknown value.</returns>
    public static string KindText(ITmLocalizer localizer, AgentGraphNodeKind kind) => kind switch
    {
        AgentGraphNodeKind.Tool => localizer["TmAgentGraph_Kind_Tool"],
        AgentGraphNodeKind.Resource => localizer["TmAgentGraph_Kind_Resource"],
        AgentGraphNodeKind.Human => localizer["TmAgentGraph_Kind_Human"],
        _ => localizer["TmAgentGraph_Kind_Agent"],
    };

    /// <summary>Whether <paramref name="kind"/> is one of the declared kinds.</summary>
    /// <param name="kind">The value to check.</param>
    /// <returns><c>true</c> for a declared member; the component paints the neutral glyph for <c>false</c>.</returns>
    public static bool IsKnownKind(AgentGraphNodeKind kind)
        => kind is >= AgentGraphNodeKind.Agent and <= AgentGraphNodeKind.Human;

    /// <summary>
    /// Wraps a node label into at most TWO lines of at most <paramref name="maxChars"/> characters, ellipsizing whatever
    /// does not fit.
    /// <para>
    /// SVG text never wraps on its own and a node box has a fixed size, so the wrap is computed here — deterministically,
    /// with no font measurement — which keeps the layout unit-testable and identical on every render. A label that
    /// still does not fit is ellipsized on the second line; the FULL label always reaches a reader through the node's
    /// accessible name and its tooltip.
    /// </para>
    /// </summary>
    /// <param name="label">The label to wrap. Null, empty or whitespace yields no lines.</param>
    /// <param name="maxChars">How many characters one line may take. Values below 2 are raised to 2.</param>
    /// <returns>Zero, one or two lines, in reading order.</returns>
    public static IReadOnlyList<string> LabelLines(string? label, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return [];
        }

        var limit = Math.Max(2, maxChars);
        var words = label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var first = TakeLine(words, 0, limit, out var next);
        if (next >= words.Length)
        {
            return [Truncate(first, limit)];
        }

        var second = TakeLine(words, next, limit, out var remaining);
        if (remaining < words.Length)
        {
            second = Truncate(second + " " + string.Join(' ', words[remaining..]), limit);
        }

        return [Truncate(first, limit), Truncate(second, limit)];
    }

    /// <summary>Greedily takes the words of one line, returning the index of the first word it did not take.</summary>
    private static string TakeLine(string[] words, int start, int limit, out int next)
    {
        var builder = new StringBuilder();
        var index = start;
        while (index < words.Length)
        {
            var candidate = builder.Length == 0 ? words[index] : builder + " " + words[index];
            if (builder.Length > 0 && candidate.Length > limit)
            {
                break;
            }

            builder.Clear();
            builder.Append(candidate);
            index++;

            if (builder.Length >= limit)
            {
                break;
            }
        }

        next = index;
        return builder.ToString();
    }

    /// <summary>Ellipsizes text that does not fit, keeping the ellipsis inside the limit.</summary>
    private static string Truncate(string text, int limit)
        => text.Length <= limit ? text : text[..Math.Max(1, limit - 1)].TrimEnd() + "…";
}
