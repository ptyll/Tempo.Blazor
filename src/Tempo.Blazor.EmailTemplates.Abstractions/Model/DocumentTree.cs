using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Model;

/// <summary>
/// Traversal helpers over the nested document structure (sections → columns → blocks, recursing into
/// hero, group and wrapper containers) plus identifier reassignment used by clone-with-new-ids.
/// </summary>
public static class DocumentTree
{
    /// <summary>Enumerates every section, including those nested inside wrapper blocks.</summary>
    /// <remarks>Iterative pre-order walk with an explicit stack (no recursion, so a deeply nested document cannot overflow the stack). Every node is visited once: an object-graph cycle built by hand terminates instead of looping.</remarks>
    public static IEnumerable<EmailSection> AllSections(EmailTemplateDocument document)
    {
        // Iterative pre-order walk (explicit stack): a hostile or broken document may nest thousands of
        // levels and recursion — also through nested iterators — would overflow the stack. A node reached
        // twice (an object-graph cycle built by hand) is visited once, so the walk always terminates.
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<EmailSection>();
        PushReversed(stack, document.Sections);
        while (stack.Count > 0)
        {
            var section = stack.Pop();
            if (!visited.Add(section)) continue;
            yield return section;

            // Same shape as before: only wrappers sitting directly in a column of the section are followed.
            var nested = new List<EmailSection>();
            foreach (var column in section.Columns)
                foreach (var block in column.Blocks)
                    if (block is EmailWrapperBlock wrapper)
                        nested.AddRange(wrapper.Sections);
            PushReversed(stack, nested);
        }
    }

    /// <summary>Enumerates every column anywhere in the document, recursing into all containers.</summary>
    /// <remarks>Iterative pre-order walk with an explicit stack (no recursion, so a deeply nested document cannot overflow the stack). Every node is visited once: an object-graph cycle built by hand terminates instead of looping.</remarks>
    public static IEnumerable<EmailColumn> AllColumns(EmailTemplateDocument document)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        for (var i = document.Sections.Count - 1; i >= 0; i--) stack.Push(document.Sections[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            switch (node)
            {
                case EmailSection section:
                    PushReversed(stack, section.Columns);
                    break;
                case EmailColumn column:
                    yield return column;
                    PushReversed(stack, column.Blocks);
                    break;
                case EmailGroupBlock group:
                    PushReversed(stack, group.Columns);
                    break;
                case EmailWrapperBlock wrapper:
                    PushReversed(stack, wrapper.Sections);
                    break;
                case EmailHeroBlock hero:
                    PushReversed(stack, hero.Blocks);
                    break;
            }
        }
    }

    /// <summary>Enumerates every block list (a column's or a hero's <c>Blocks</c>) in the document.</summary>
    /// <remarks>Iterative pre-order walk with an explicit stack (no recursion, so a deeply nested document cannot overflow the stack). Every node is visited once: an object-graph cycle built by hand terminates instead of looping.</remarks>
    public static IEnumerable<IList<EmailBlockBase>> AllBlockLists(EmailTemplateDocument document)
    {
        var acc = new List<IList<EmailBlockBase>>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IList<EmailBlockBase>>();
        for (var s = document.Sections.Count - 1; s >= 0; s--)
        {
            var columns = document.Sections[s].Columns;
            for (var c = columns.Count - 1; c >= 0; c--) stack.Push(columns[c].Blocks);
        }

        var children = new List<IList<EmailBlockBase>>();
        while (stack.Count > 0)
        {
            var list = stack.Pop();
            if (!visited.Add(list)) continue;
            acc.Add(list);

            children.Clear();
            foreach (var block in list)
            {
                switch (block)
                {
                    case EmailHeroBlock hero:
                        children.Add(hero.Blocks);
                        break;
                    case EmailGroupBlock group:
                        foreach (var column in group.Columns) children.Add(column.Blocks);
                        break;
                    case EmailWrapperBlock wrapper:
                        foreach (var section in wrapper.Sections)
                            foreach (var column in section.Columns) children.Add(column.Blocks);
                        break;
                }
            }

            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }

        return acc;
    }

    /// <summary>Enumerates every block anywhere in the document.</summary>
    /// <remarks>Built on <see cref="AllBlockLists"/>, so it shares its iterative, visit-once traversal.</remarks>
    public static IEnumerable<EmailBlockBase> AllBlocks(EmailTemplateDocument document)
        => AllBlockLists(document).SelectMany(list => list);

    /// <summary>Assigns a fresh identifier to a block and to every node nested beneath it.</summary>
    /// <remarks>Iterative walk with an explicit stack and a visit-once guard: a deeply nested or cyclic document neither overflows the stack nor loops. Identifiers are reassigned in pre-order; a node reachable twice is renumbered once.</remarks>
    public static void ReassignIds(EmailBlockBase block) => ReassignIdsIterative(block);

    /// <summary>Assigns a fresh identifier to a section and everything beneath it.</summary>
    /// <remarks>Iterative walk with an explicit stack and a visit-once guard: a deeply nested or cyclic document neither overflows the stack nor loops. Identifiers are reassigned in pre-order; a node reachable twice is renumbered once.</remarks>
    public static void ReassignIds(EmailSection section) => ReassignIdsIterative(section);

    /// <summary>Assigns a fresh identifier to a column and every block beneath it.</summary>
    /// <remarks>Iterative walk with an explicit stack and a visit-once guard: a deeply nested or cyclic document neither overflows the stack nor loops. Identifiers are reassigned in pre-order; a node reachable twice is renumbered once.</remarks>
    public static void ReassignIds(EmailColumn column) => ReassignIdsIterative(column);

    private static void ReassignIdsIterative(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            switch (node)
            {
                case EmailSection section:
                    section.Id = Guid.NewGuid();
                    PushReversed(stack, section.Columns);
                    break;
                case EmailColumn column:
                    column.Id = Guid.NewGuid();
                    PushReversed(stack, column.Blocks);
                    break;
                case EmailBlockBase block:
                    block.Id = Guid.NewGuid();
                    switch (block)
                    {
                        case EmailHeroBlock hero:
                            PushReversed(stack, hero.Blocks);
                            break;
                        case EmailGroupBlock group:
                            PushReversed(stack, group.Columns);
                            break;
                        case EmailWrapperBlock wrapper:
                            PushReversed(stack, wrapper.Sections);
                            break;
                    }

                    break;
            }
        }
    }

    private static void PushReversed<TItem>(Stack<TItem> stack, IList<TItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--) stack.Push(items[i]);
    }

    private static void PushReversed<TItem>(Stack<object> stack, IList<TItem> items) where TItem : class
    {
        for (var i = items.Count - 1; i >= 0; i--) stack.Push(items[i]);
    }
}
