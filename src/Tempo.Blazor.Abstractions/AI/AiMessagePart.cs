using System.Text;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One ordered piece of an <see cref="AiMessage"/>. The concrete kinds (<see cref="AiTextPart"/>,
/// <see cref="AiReasoningPart"/>, <see cref="AiToolCallPart"/>, <see cref="AiSourcesPart"/>) are rendered
/// in the order they arrived, so a message can interleave reasoning, tool calls and text.
/// </summary>
public abstract class AiMessagePart
{
    /// <summary>Creates the part; used by the concrete kinds only.</summary>
    protected AiMessagePart()
    {
    }
}

/// <summary>Markdown text of a message.</summary>
public sealed class AiTextPart : AiMessagePart
{
    private readonly StringBuilder _markdown = new();

    /// <summary>Creates an empty text part.</summary>
    public AiTextPart()
    {
    }

    /// <summary>Creates a text part with initial markdown.</summary>
    /// <param name="markdown">Initial markdown content.</param>
    public AiTextPart(string? markdown)
    {
        _markdown.Append(markdown);
    }

    /// <summary>The markdown received so far. Untrusted model output — render it through <c>TmMarkdownView</c>.</summary>
    public string Markdown
    {
        get => _markdown.ToString();
        set
        {
            _markdown.Clear();
            _markdown.Append(value);
        }
    }

    /// <summary>Appends streamed text.</summary>
    /// <param name="text">Text chunk; <see langword="null"/> is ignored.</param>
    public void Append(string? text) => _markdown.Append(text);
}

/// <summary>Reasoning ("thinking") of an assistant message: ordered steps, optional sources and elapsed time.</summary>
public sealed class AiReasoningPart : AiMessagePart
{
    private readonly List<string> _steps = [];
    private readonly List<AiSource> _sources = [];

    /// <summary>Creates an empty reasoning part.</summary>
    public AiReasoningPart()
    {
    }

    /// <summary>Creates a reasoning part with the supplied steps.</summary>
    /// <param name="steps">Reasoning steps, in order.</param>
    /// <param name="sources">Sources the model inspected while reasoning.</param>
    public AiReasoningPart(IEnumerable<string>? steps, IEnumerable<AiSource>? sources = null)
    {
        if (steps is not null)
        {
            _steps.AddRange(steps);
        }

        if (sources is not null)
        {
            _sources.AddRange(sources);
        }
    }

    /// <summary>Ordered reasoning steps; the trailing step is the one still being streamed.</summary>
    public IList<string> Steps => _steps;

    /// <summary>Sources inspected during the reasoning.</summary>
    public IReadOnlyList<AiSource> Sources => _sources;

    /// <summary>How long the model reasoned, when the host reported it.</summary>
    public double? DurationMs { get; set; }

    /// <summary>Appends sources, skipping ids that are already present.</summary>
    /// <param name="sources">Sources to append.</param>
    public void AddSources(IEnumerable<AiSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var source in sources)
        {
            if (!_sources.Any(existing => string.Equals(existing.Id, source.Id, StringComparison.Ordinal)))
            {
                _sources.Add(source);
            }
        }
    }
}

/// <summary>One tool call inside a message.</summary>
public sealed class AiToolCallPart : AiMessagePart
{
    /// <summary>Creates a tool-call part.</summary>
    /// <param name="toolCall">The call this part renders.</param>
    public AiToolCallPart(AiToolCall toolCall)
    {
        ToolCall = toolCall ?? throw new ArgumentNullException(nameof(toolCall));
    }

    /// <summary>The streamed call state.</summary>
    public AiToolCall ToolCall { get; }
}

/// <summary>The sources cited by the answer, rendered as source cards.</summary>
public sealed class AiSourcesPart : AiMessagePart
{
    private readonly List<AiSource> _sources = [];

    /// <summary>Creates an empty sources part.</summary>
    public AiSourcesPart()
    {
    }

    /// <summary>Creates a sources part from the supplied sources.</summary>
    /// <param name="sources">Cited sources, in citation order.</param>
    public AiSourcesPart(IEnumerable<AiSource>? sources)
    {
        if (sources is not null)
        {
            _sources.AddRange(sources);
        }
    }

    /// <summary>Cited sources, in the order they were first cited.</summary>
    public IReadOnlyList<AiSource> Sources => _sources;

    /// <summary>Adds a source unless a source with the same id is already cited.</summary>
    /// <param name="source">The source to add.</param>
    /// <returns><see langword="true"/> when the source was added.</returns>
    public bool Add(AiSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.Any(existing => string.Equals(existing.Id, source.Id, StringComparison.Ordinal)))
        {
            return false;
        }

        _sources.Add(source);
        return true;
    }
}
