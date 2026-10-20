using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Wraps Scriban's accessor for a live CLR object and refuses values that would turn a data read into
/// code execution: Scriban invokes any <see cref="Delegate"/> it reads as a function, so a delegate
/// reaching a template would let the author call host code with template-chosen arguments. Delegates
/// handed to the template on purpose (the host function registry) live in a script object, which is
/// never wrapped.
/// </summary>
internal sealed class SandboxedObjectAccessor(IObjectAccessor inner) : IObjectAccessor
{
    public bool HasIndexer => inner.HasIndexer;

    public Type? IndexType => inner.IndexType;

    public int GetMemberCount(TemplateContext context, SourceSpan span, object target)
        => inner.GetMemberCount(context, span, target);

    public IEnumerable<string> GetMembers(TemplateContext context, SourceSpan span, object target)
        => inner.GetMembers(context, span, target);

    public bool HasMember(TemplateContext context, SourceSpan span, object target, string member)
        => inner.HasMember(context, span, target, member);

    public bool TryGetValue(
        TemplateContext context,
        SourceSpan span,
        object target,
        string member,
        out object? value)
    {
        if (!inner.TryGetValue(context, span, target, member, out value))
        {
            return false;
        }

        EnsureNotCallable(target, member, value);
        return true;
    }

    public bool TrySetValue(
        TemplateContext context,
        SourceSpan span,
        object target,
        string member,
        object? value)
        => inner.TrySetValue(context, span, target, member, value);

    public bool TryGetItem(
        TemplateContext context,
        SourceSpan span,
        object target,
        object index,
        out object? value)
    {
        if (!inner.TryGetItem(context, span, target, index, out value))
        {
            return false;
        }

        EnsureNotCallable(target, index?.ToString() ?? "<index>", value);
        return true;
    }

    public bool TrySetItem(
        TemplateContext context,
        SourceSpan span,
        object target,
        object index,
        object? value)
        => inner.TrySetItem(context, span, target, index, value);

    private static void EnsureNotCallable(object target, string member, object? value)
    {
        if (value is not Delegate)
        {
            return;
        }

        throw new TemplateSecurityViolationException(
            TemplateSecurityEventKind.BlockedMember,
            $"{target.GetType().Name}.{member}");
    }
}
