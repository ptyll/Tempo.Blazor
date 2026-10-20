using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Model;

/// <summary>
/// Iterative block-nesting measurement over the document tree. A block directly in a section column has
/// depth 1 and every hero/group/wrapper level adds one — the convention
/// <see cref="Layout.LayoutValidator"/> uses. The walk is iterative (an explicit stack) on purpose: a
/// hostile or broken document may nest thousands of levels, and recursion would overflow the stack.
/// </summary>
internal static class DocumentNesting
{
    /// <summary>
    /// Returns the first block that sits deeper than <paramref name="maxDepth"/>, together with its
    /// depth. A block reachable twice (an object-graph cycle a host built by hand) counts as too deep, so
    /// the walk always terminates.
    /// </summary>
    public static (EmailBlockBase Block, int Depth)? FirstTooDeep(EmailTemplateDocument document, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(document);

        var visited = new HashSet<EmailBlockBase>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(EmailBlockBase Block, int Depth)>();
        PushChildren(document, stack, depth: 1);

        while (stack.Count > 0)
        {
            var (block, depth) = stack.Pop();
            if (depth > maxDepth || !visited.Add(block))
            {
                return (block, depth);
            }

            PushChildren(block, stack, depth + 1);
        }

        return null;
    }

    /// <summary>
    /// The deepest block depth of the document, or <paramref name="cap"/> + 1 when it nests deeper (or
    /// contains a cycle) — callers use that to fail with a clear error instead of recursing.
    /// </summary>
    public static int MaxDepth(EmailTemplateDocument document, int cap)
    {
        ArgumentNullException.ThrowIfNull(document);

        var visited = new HashSet<EmailBlockBase>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(EmailBlockBase Block, int Depth)>();
        PushChildren(document, stack, depth: 1);

        var max = 0;
        while (stack.Count > 0)
        {
            var (block, depth) = stack.Pop();
            if (depth > cap || !visited.Add(block))
            {
                return cap + 1;
            }

            max = Math.Max(max, depth);
            PushChildren(block, stack, depth + 1);
        }

        return max;
    }

    private static void PushChildren(
        EmailTemplateDocument document,
        Stack<(EmailBlockBase Block, int Depth)> stack,
        int depth)
    {
        foreach (var section in document.Sections)
        {
            PushChildren(section, stack, depth);
        }
    }

    private static void PushChildren(
        EmailBlockBase block,
        Stack<(EmailBlockBase Block, int Depth)> stack,
        int depth)
    {
        foreach (var child in Children(block))
        {
            stack.Push((child, depth));
        }
    }

    private static void PushChildren(
        EmailSection section,
        Stack<(EmailBlockBase Block, int Depth)> stack,
        int depth)
    {
        foreach (var column in section.Columns)
        {
            foreach (var block in column.Blocks)
            {
                stack.Push((block, depth));
            }
        }
    }

    private static IEnumerable<EmailBlockBase> Children(EmailBlockBase block) => block switch
    {
        EmailHeroBlock hero => hero.Blocks,
        EmailGroupBlock group => group.Columns.SelectMany(column => column.Blocks),
        EmailWrapperBlock wrapper => wrapper.Sections.SelectMany(section => section.Columns).SelectMany(column => column.Blocks),
        _ => [],
    };
}
