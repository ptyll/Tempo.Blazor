using Scriban;
using Scriban.Syntax;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The authoritative <see cref="ITemplateVariableExtractor"/>: extracts the variable paths referenced by
/// a template by walking the parsed Scriban AST. Loop variables are excluded automatically (they parse as
/// locals, not globals); loop pseudo-objects (<c>for</c>, <c>while</c>…) are ignored explicitly. Both the
/// full dotted path and its root are reported.
/// </summary>
/// <remarks>
/// Registered by <c>AddTempoEmailTemplateEngine()</c>, which replaces the BCL baseline
/// <see cref="RegexTemplateVariableExtractor"/> the model registration installs. The difference is only
/// detection quality (filters, loop-local variables and members the regex cannot see) — never a security
/// decision.
/// </remarks>
public sealed class ScribanTemplateVariableExtractor : ITemplateVariableExtractor
{
    /// <summary>The shared, stateless instance.</summary>
    public static ScribanTemplateVariableExtractor Instance { get; } = new();

    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
        { "for", "while", "tablerow", "loop", "this", "empty" };

    /// <inheritdoc />
    public IReadOnlyList<string> Extract(string template)
        => ExtractInfos(template).Select(i => i.Path).ToList();

    /// <inheritdoc />
    public IReadOnlyList<TemplateVariableInfo> ExtractInfos(string template)
    {
        var parsed = Template.Parse(template);
        if (parsed.HasErrors || parsed.Page is null) return Array.Empty<TemplateVariableInfo>();

        var visitor = new VariableVisitor();
        visitor.Visit(parsed.Page);
        return visitor.BuildInfos();
    }

    private sealed class VariableVisitor : ScriptVisitor
    {
        private readonly List<string> _results = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly HashSet<string> _collections = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _locals = new(StringComparer.Ordinal);

        public IReadOnlyList<TemplateVariableInfo> BuildInfos()
            => _results.Select(path => new TemplateVariableInfo(
                path, _collections.Contains(path) ? VariableKind.Collection : VariableKind.Scalar)).ToList();

        public override void Visit(ScriptVariableGlobal node) => RecordRoot(node.Name);

        public override void Visit(ScriptForStatement node)
        {
            if (node.Iterator is not null && TryResolve(node.Iterator, out var path, out var root))
            {
                _collections.Add(path);
                Record(root, path);
            }
            else if (node.Iterator is not null)
            {
                Visit(node.Iterator);
            }

            var localName = (node.Variable as ScriptVariable)?.Name;
            if (localName is not null) PushLocal(localName);
            if (node.Body is not null) Visit(node.Body);
            if (localName is not null) PopLocal(localName);
            if (node.Else is not null) Visit(node.Else);
        }

        private void PushLocal(string name)
            => _locals[name] = _locals.TryGetValue(name, out var c) ? c + 1 : 1;

        private void PopLocal(string name)
        {
            if (_locals.TryGetValue(name, out var c) && c > 1) _locals[name] = c - 1;
            else _locals.Remove(name);
        }

        private bool IsLocal(string root) => _locals.ContainsKey(root);

        public override void Visit(ScriptMemberExpression node)
        {
            if (TryResolve(node, out var path, out var root))
                Record(root, path);
            else
                base.Visit(node);
        }

        public override void Visit(ScriptIndexerExpression node)
        {
            if (TryResolve(node, out var path, out var root))
                Record(root, path);
            else
                base.Visit(node.Target);

            if (node.Index is not null) Visit(node.Index); // the index expression may reference variables
        }

        private static bool TryResolve(ScriptExpression expression, out string path, out string root)
        {
            path = string.Empty;
            root = string.Empty;
            var parts = new List<string>();
            ScriptExpression? current = expression;
            while (current is not null)
            {
                switch (current)
                {
                    case ScriptMemberExpression { Member.Name: { } memberName } member:
                        parts.Add(memberName);
                        current = member.Target;
                        break;
                    case ScriptIndexerExpression indexer:
                        current = indexer.Target; // the index itself is visited separately
                        break;
                    case ScriptVariableGlobal global:
                        root = global.Name;
                        parts.Add(global.Name);
                        parts.Reverse();
                        path = string.Join('.', parts);
                        return true;
                    default:
                        return false; // target is not a plain variable (e.g. a function call)
                }
            }
            return false;
        }

        private void Record(string root, string path)
        {
            if (Ignored.Contains(root) || IsLocal(root)) return;
            Add(root);
            Add(path);
        }

        private void RecordRoot(string name)
        {
            if (Ignored.Contains(name) || IsLocal(name)) return;
            Add(name);
        }

        private void Add(string value)
        {
            if (_seen.Add(value)) _results.Add(value);
        }
    }
}
