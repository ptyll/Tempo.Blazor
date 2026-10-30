using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Default submitter-role colours shared by <see cref="TmRecipientRoleEditor"/> (which serializes
/// them into templates as the colour of every newly created role) and the field editor's colour
/// input fallback.
/// <para>
/// The values are the light-theme counterparts of the <c>--tm-signing-role-1..6</c> tokens declared
/// in the Signing stylesheet, but they stay hex ON PURPOSE: they are serialized data — a stored
/// template carries the colour string verbatim, and the value feeds <c>&lt;input type="color"&gt;</c>,
/// which accepts concrete colours only. Hosts rebrand the CSS side by overriding the tokens —
/// <see cref="ToCssColor"/> maps every default-palette colour onto its token reference, so a
/// rebrand reaches stored default colours without touching template data; a host that wants new
/// templates to start from its own palette assigns <see cref="SigningSubmitterRole.Color"/> itself
/// before handing roles to the editors.
/// </para>
/// </summary>
public static class SigningRolePalette
{
    /// <summary>
    /// Role 6's colour before phase 1 darkened it to <c>#a16207</c> — stored templates may still
    /// carry it, so it stays an alias of the role-6 token.
    /// </summary>
    private const string LegacyRole6Color = "#ca8a04";

    /// <summary>Default role colours, indexed by the zero-based role order.</summary>
    public static readonly IReadOnlyList<string> DefaultColors =
    [
        "#2563eb",
        "#16a34a",
        "#dc2626",
        "#9333ea",
        "#0891b2",
        "#a16207"
    ];

    /// <summary>The default colour for the role at zero-based <paramref name="order"/> (wraps around).</summary>
    public static string ColorForOrder(int order)
    {
        var index = ((order % DefaultColors.Count) + DefaultColors.Count) % DefaultColors.Count;
        return DefaultColors[index];
    }

    /// <summary>
    /// Maps a stored role colour onto the stylesheet: a default-palette value (case-insensitive)
    /// becomes the matching <c>var(--tm-signing-role-N)</c> token reference — hosts that rebrand the
    /// tokens then rebrand every default-coloured role at once — the pre-phase-1 role-6 colour
    /// <c>#ca8a04</c> (kept by stored templates) maps onto <c>var(--tm-signing-role-6)</c> like its
    /// successor, and any other value passes through verbatim.
    /// </summary>
    public static string? ToCssColor(string? color)
    {
        for (var i = 0; i < DefaultColors.Count; i++)
        {
            if (string.Equals(DefaultColors[i], color, StringComparison.OrdinalIgnoreCase))
            {
                return $"var(--tm-signing-role-{i + 1})";
            }
        }

        if (string.Equals(LegacyRole6Color, color, StringComparison.OrdinalIgnoreCase))
        {
            return "var(--tm-signing-role-6)";
        }

        return color;
    }

    /// <summary>
    /// The CSS colour of the submitter role that owns <paramref name="submitterUuid"/>: the role's
    /// stored colour through <see cref="ToCssColor"/>, or the palette token of the role's order when
    /// the role carries no colour. <c>null</c> when no role matches — the overlay keeps its
    /// primary-tint default.
    /// </summary>
    internal static string? ResolveRoleColor(IReadOnlyList<SigningSubmitterRole> roles, string? submitterUuid) =>
        TryFindRole(roles, submitterUuid, out var role, out var order)
            ? role.Color is null
                ? $"var(--tm-signing-role-{(order % DefaultColors.Count) + 1})"
                : ToCssColor(role.Color)
            : null;

    /// <summary>
    /// The concrete colour of the submitter role that owns <paramref name="submitterUuid"/>, for the
    /// field editor's <c>&lt;input type="color"&gt;</c> (it cannot render a <c>var()</c> reference):
    /// the role's stored colour, else the palette colour of the role's order. <c>null</c> when no
    /// role matches.
    /// </summary>
    internal static string? ResolveRoleHexColor(IReadOnlyList<SigningSubmitterRole> roles, string? submitterUuid) =>
        TryFindRole(roles, submitterUuid, out var role, out var order)
            ? role.Color ?? ColorForOrder(order)
            : null;

    private static bool TryFindRole(
        IReadOnlyList<SigningSubmitterRole> roles,
        string? submitterUuid,
        out SigningSubmitterRole role,
        out int order)
    {
        role = null!;
        order = -1;
        if (roles.Count == 0 || string.IsNullOrWhiteSpace(submitterUuid))
        {
            return false;
        }

        for (var i = 0; i < roles.Count; i++)
        {
            if (string.Equals(roles[i].Uuid, submitterUuid, StringComparison.Ordinal))
            {
                role = roles[i];
                order = i;
                return true;
            }
        }

        return false;
    }
}
