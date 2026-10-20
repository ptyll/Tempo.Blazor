using System.Collections.Concurrent;
using System.Reflection;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>The sandbox verdict for one reflected member.</summary>
internal enum SandboxMemberVerdict
{
    Allowed,
    BlockedMember,
    BlockedNamespace,
}

/// <summary>
/// The template sandbox member policy. Deliberately hard-coded and <b>not</b> configurable by a host:
/// a host must never be able to opt back into reflection. It mirrors the PostrionCore sandbox
/// (<c>SandboxedTemplateContext.IsMemberAllowed</c>) and is applied in three layers —
/// <see cref="Scriban.Runtime.ScriptObject"/> import, Scriban's <c>MemberFilter</c> and the runtime
/// lookup probe that turns a refused access into a render error.
/// </summary>
internal static class SandboxMemberPolicy
{
    /// <summary>
    /// Namespaces whose types must never be reachable from a template. A member declared on a type
    /// from one of these namespaces is refused no matter how the template got hold of the value.
    /// </summary>
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "System.Reflection",
        "System.IO",
        "System.Diagnostics",
        "System.Net",
        "System.Runtime",
        "System.Threading",
    ];

    /// <summary>CLR member names that are refused on any type, whatever it declares.</summary>
    private static readonly HashSet<string> ForbiddenClrMemberNames = new(StringComparer.Ordinal)
    {
        "GetType",
        "GetTypeFromCLSID",
        "GetTypeFromHandle",
        "GetTypeFromProgID",
        "GetMembers",
        "GetMember",
        "GetMethods",
        "GetMethod",
        "GetProperties",
        "GetProperty",
        "GetFields",
        "GetField",
        "GetNestedTypes",
        "GetInterfaces",
        "GetConstructors",
        "GetConstructor",
        "GetEvents",
        "GetEvent",
        "GetCustomAttributes",
        "GetCustomAttribute",
        "GetGenericArguments",
        "GetGenericTypeDefinition",
        "GetILAsByteArray",
        "Assembly",
        "AssemblyQualifiedName",
        "Module",
        "TypeHandle",
        "MethodHandle",
        "FieldHandle",
        "PropertyHandle",
        "EventHandle",
        "UnderlyingSystemType",
        "DeclaringType",
        "ReflectedType",
        "MetadataToken",
        "InvokeMember",
        "CreateInstance",
        "DynamicInvoke",
    };

    /// <summary>
    /// The same names in script form (<c>GetType</c> → <c>get_type</c>) — the form a template author
    /// writes. A refused name is refused in both spellings.
    /// </summary>
    private static readonly HashSet<string> ForbiddenScriptMemberNames = BuildScriptNames();

    /// <summary>
    /// The subset of refused names that the pre-render text scan looks for. Deliberately narrower than
    /// <see cref="ForbiddenClrMemberNames"/>: a data identifier such as <c>module</c> or
    /// <c>assembly</c> is a plausible model key, and those names are already refused loudly by the
    /// runtime lookup probe when they really are a member access. What stays here are the reflection
    /// entry points no legitimate e-mail template calls.
    /// </summary>
    private static readonly HashSet<string> ScannedScriptMemberNames = new(StringComparer.Ordinal)
    {
        "GetType", "get_type",
        "GetTypeFromHandle", "get_type_from_handle",
        "GetTypeFromProgID", "get_type_from_prog_id",
        "GetTypeFromCLSID", "get_type_from_clsid",
        "GetMembers", "get_members",
        "GetMember", "get_member",
        "GetMethods", "get_methods",
        "GetMethod", "get_method",
        "GetProperties", "get_properties",
        "GetProperty", "get_property",
        "GetFields", "get_fields",
        "GetField", "get_field",
        "GetNestedTypes", "get_nested_types",
        "InvokeMember", "invoke_member",
        "CreateInstance", "create_instance",
        "DynamicInvoke", "dynamic_invoke",
    };

    /// <summary>Per-type map of script member name → refusal, built once and cached.</summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, (SandboxMemberVerdict Verdict, string Detail)>> BlockedMembersByType = new();

    /// <summary>
    /// Decides whether a reflected member may be read from a template.
    /// </summary>
    public static bool IsMemberAllowed(MemberInfo? member) => GetVerdict(member) == SandboxMemberVerdict.Allowed;

    /// <summary>
    /// True when the identifier is a refused member name in either CLR (<c>GetType</c>) or script
    /// (<c>get_type</c>) spelling.
    /// </summary>
    public static bool IsForbiddenName(string identifier) => ForbiddenScriptMemberNames.Contains(identifier);

    /// <summary>True when the identifier is worth refusing from the template source alone.</summary>
    public static bool IsScannedName(string identifier) => ScannedScriptMemberNames.Contains(identifier);

    /// <summary>Classifies a single reflected member.</summary>
    public static SandboxMemberVerdict GetVerdict(MemberInfo? member)
    {
        if (member is null)
        {
            return SandboxMemberVerdict.BlockedMember;
        }

        if (IsForbiddenDeclaringType(member.DeclaringType))
        {
            return SandboxMemberVerdict.BlockedNamespace;
        }

        if (ForbiddenClrMemberNames.Contains(member.Name))
        {
            return SandboxMemberVerdict.BlockedMember;
        }

        if (IsDelegateValued(member))
        {
            return SandboxMemberVerdict.BlockedMember;
        }

        return SandboxMemberVerdict.Allowed;
    }

    /// <summary>
    /// A member that hands out a <see cref="Delegate"/> would let a template call arbitrary host code
    /// (Scriban invokes any function value it reads), so it is refused rather than exposed as data.
    /// </summary>
    private static bool IsDelegateValued(MemberInfo member) => member switch
    {
        PropertyInfo property => !property.GetIndexParameters().Any() &&
                                 typeof(Delegate).IsAssignableFrom(property.PropertyType),
        FieldInfo field => typeof(Delegate).IsAssignableFrom(field.FieldType),
        _ => false,
    };

    /// <summary>
    /// Refuses members declared on the reflection types themselves and on any type from a forbidden
    /// namespace.
    /// </summary>
    public static bool IsForbiddenDeclaringType(Type? declaringType)
    {
        if (declaringType is null)
        {
            return true;
        }

        if (typeof(Type).IsAssignableFrom(declaringType) ||
            typeof(MemberInfo).IsAssignableFrom(declaringType) ||
            typeof(Assembly).IsAssignableFrom(declaringType) ||
            typeof(Module).IsAssignableFrom(declaringType))
        {
            return true;
        }

        return IsForbiddenNamespace(declaringType.Namespace);
    }

    /// <summary>True when the namespace is (or lives under) a forbidden namespace.</summary>
    public static bool IsForbiddenNamespace(string? @namespace)
    {
        if (string.IsNullOrEmpty(@namespace))
        {
            return false;
        }

        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (@namespace!.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Classifies a member lookup that Scriban could not resolve on <paramref name="targetType"/>.
    /// Returns <see langword="null"/> when the name is not refused, so ordinary "missing data" keeps
    /// the lenient rendering behaviour.
    /// </summary>
    public static (SandboxMemberVerdict Verdict, string Detail)? ClassifyFailedLookup(Type targetType, string scriptMemberName)
    {
        if (IsForbiddenDeclaringType(targetType))
        {
            return (SandboxMemberVerdict.BlockedNamespace, DescribeForbiddenType(targetType));
        }

        var blocked = BlockedMembersByType.GetOrAdd(targetType, BuildBlockedMemberMap);
        if (blocked.TryGetValue(scriptMemberName, out var hit))
        {
            return hit;
        }

        if (ForbiddenScriptMemberNames.Contains(scriptMemberName))
        {
            return (SandboxMemberVerdict.BlockedMember, scriptMemberName);
        }

        return null;
    }

    /// <summary>
    /// Describes a refused type the way a template author sees it: <c>Namespace.Name</c> rather than
    /// the reflection <see cref="Type.FullName"/> (which carries generic arguments and nested-type
    /// separators and would leak assembly-qualified metadata into the audit trail).
    /// </summary>
    internal static string DescribeForbiddenType(Type type)
    {
        if (typeof(Type).IsAssignableFrom(type)) return nameof(System.Type);
        if (typeof(MemberInfo).IsAssignableFrom(type)) return nameof(System.Reflection.MemberInfo);
        if (typeof(Assembly).IsAssignableFrom(type)) return nameof(System.Reflection.Assembly);
        if (typeof(Module).IsAssignableFrom(type)) return nameof(System.Reflection.Module);
        return $"{type.Namespace}.{type.Name}";
    }

    /// <summary>Renames a CLR member name the way the sandbox's standard renamer does.</summary>
    public static string ToScriptName(string clrMemberName) => StandardMemberRenamer.Rename(clrMemberName);

    private static IReadOnlyDictionary<string, (SandboxMemberVerdict Verdict, string Detail)> BuildBlockedMemberMap(Type type)
    {
        var blocked = new Dictionary<string, (SandboxMemberVerdict, string)>(StringComparer.Ordinal);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(Flags))
            {
                if (property.CanRead)
                {
                    Add(blocked, property);
                }
            }

            foreach (var field in current.GetFields(Flags))
            {
                if (!field.IsStatic && !field.IsLiteral)
                {
                    Add(blocked, field);
                }
            }
        }

        return blocked;

        static void Add(Dictionary<string, (SandboxMemberVerdict, string)> blocked, MemberInfo member)
        {
            var verdict = GetVerdict(member);
            if (verdict == SandboxMemberVerdict.Allowed)
            {
                return;
            }

            var detail = member.DeclaringType is { } declaringType
                ? $"{declaringType.FullName}.{member.Name}"
                : member.Name;
            blocked[ToScriptName(member.Name)] = (verdict, detail);
        }
    }

    private static HashSet<string> BuildScriptNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in ForbiddenClrMemberNames)
        {
            names.Add(name);
            names.Add(ToScriptName(name));
        }

        return names;
    }
}
