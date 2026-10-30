using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Edits signing template roles and concrete submission recipient details.</summary>
/// <remarks>
/// Newly created roles default to the <see cref="SigningRolePalette"/> colours. Those hex values are
/// serialized template data — a saved template stores the colour string verbatim — and mirror the
/// <c>--tm-signing-role-1..6</c> tokens declared in the Signing stylesheet; the tokens exist so CSS
/// can paint role colours (and so hosts can rebrand the palette), while this editor keeps writing
/// concrete colours into the model.
/// </remarks>
public partial class TmRecipientRoleEditor
{
    private readonly List<SigningSubmitterRole> _roles = [];
    private IReadOnlyList<SigningSubmitterRole>? _lastRoles;
    private int? _dragSourceIndex;

    /// <summary>Roles or submission recipients currently edited.</summary>
    [Parameter] public IReadOnlyList<SigningSubmitterRole> Roles { get; set; } = [];

    /// <summary>Callback invoked whenever roles change.</summary>
    [Parameter] public EventCallback<IReadOnlyList<SigningSubmitterRole>> RolesChanged { get; set; }

    /// <summary>Whether the editor edits template roles or concrete submission recipients.</summary>
    [Parameter] public TmRecipientRoleEditorMode Mode { get; set; }

    /// <summary>Fields available for invite-via-field rules.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Whether the editor controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private bool HasValidationErrors => _roles.Any(IsEmailMissing);

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-recipient-role-editor" };
            AddClass(classes, Disabled, "tm-recipient-role-editor--disabled");
            AddClass(classes, HasValidationErrors, "tm-recipient-role-editor--invalid");
            AddClass(classes, Mode == TmRecipientRoleEditorMode.TemplateRoles, "tm-recipient-role-editor--template");
            AddClass(classes, Mode == TmRecipientRoleEditorMode.SubmissionRecipients, "tm-recipient-role-editor--submission");

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    private string GetRowClass(SigningSubmitterRole role)
    {
        var classes = new List<string> { "tm-recipient-role-editor__row" };
        AddClass(classes, IsEmailMissing(role), "tm-recipient-role-editor__row--invalid");
        return string.Join(" ", classes);
    }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastRoles, Roles))
        {
            _roles.Clear();
            _roles.AddRange(NormalizeRoles(Roles));

            if (_roles.Count == 0)
            {
                _roles.Add(CreateDefaultRole(0));
            }

            _lastRoles = Roles;
        }
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private IEnumerable<SigningSubmitterRole> GetOtherRoles(SigningSubmitterRole role)
    {
        return _roles.Where(candidate => !string.Equals(candidate.Uuid, role.Uuid, StringComparison.Ordinal));
    }

    private bool IsEmailMissing(SigningSubmitterRole role)
    {
        return Mode == TmRecipientRoleEditorMode.SubmissionRecipients
            && !role.IsOptional
            && string.IsNullOrWhiteSpace(role.Email);
    }

    private async Task AddRoleAsync()
    {
        if (Disabled)
        {
            return;
        }

        _roles.Add(CreateDefaultRole(_roles.Count));
        await NotifyChangedAsync();
    }

    private async Task RemoveRoleAsync(int index)
    {
        if (Disabled || !TryGetRole(index, out _))
        {
            return;
        }

        _roles.RemoveAt(index);
        if (_roles.Count == 0)
        {
            _roles.Add(CreateDefaultRole(0));
        }

        await NotifyChangedAsync();
    }

    private async Task MoveRoleAsync(int index, int delta)
    {
        if (Disabled || !TryGetRole(index, out _))
        {
            return;
        }

        var target = index + delta;
        if (target < 0 || target >= _roles.Count)
        {
            return;
        }

        (_roles[index], _roles[target]) = (_roles[target], _roles[index]);
        await NotifyChangedAsync();
    }

    private void HandleDragStart(int index)
    {
        _dragSourceIndex = Disabled || !TryGetRole(index, out _)
            ? null
            : index;
    }

    private void HandleDragEnd()
    {
        _dragSourceIndex = null;
    }

    private async Task DropRoleAsync(int targetIndex)
    {
        if (Disabled || _dragSourceIndex is not { } sourceIndex || !TryGetRole(sourceIndex, out var role) || !TryGetRole(targetIndex, out _))
        {
            return;
        }

        _roles.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        _roles.Insert(targetIndex, role);
        _dragSourceIndex = null;
        await NotifyChangedAsync();
    }

    private Task HandleNameChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role => role.Name = args.Value?.ToString() ?? string.Empty);
    }

    private Task HandleColorChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role => role.Color = NormalizeColor(args.Value?.ToString(), index));
    }

    private Task HandleFullNameChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role => role.FullName = NormalizeOptional(args.Value?.ToString()));
    }

    private Task HandleEmailChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role => role.Email = NormalizeOptional(args.Value?.ToString()));
    }

    private Task HandlePhoneChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role => role.Phone = NormalizeOptional(args.Value?.ToString()));
    }

    private Task HandleInviteByRoleChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role =>
        {
            role.InviteByRoleUuid = NormalizeRoleReference(role, args.Value?.ToString());
        });
    }

    private Task HandleOptionalInviteByRoleChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role =>
        {
            role.OptionalInviteByRoleUuid = NormalizeRoleReference(role, args.Value?.ToString());
        });
    }

    private Task HandleInviteViaFieldChangedAsync(int index, ChangeEventArgs args)
    {
        return UpdateRoleAsync(index, role =>
        {
            var value = NormalizeOptional(args.Value?.ToString());
            role.InviteViaFieldUuid = Fields.Any(field => string.Equals(field.Uuid, value, StringComparison.Ordinal))
                ? value
                : null;
        });
    }

    private async Task UpdateRoleAsync(int index, Action<SigningSubmitterRole> update)
    {
        if (Disabled || !TryGetRole(index, out var role))
        {
            return;
        }

        update(role);
        await NotifyChangedAsync();
    }

    private bool TryGetRole(int index, out SigningSubmitterRole role)
    {
        if (index < 0 || index >= _roles.Count)
        {
            role = default!;
            return false;
        }

        role = _roles[index];
        return true;
    }

    private Task NotifyChangedAsync()
    {
        for (var index = 0; index < _roles.Count; index++)
        {
            _roles[index].Order = index;
        }

        var normalized = NormalizeRoles(_roles).ToArray();
        _roles.Clear();
        _roles.AddRange(normalized);
        return RolesChanged.InvokeAsync(normalized);
    }

    private IEnumerable<SigningSubmitterRole> NormalizeRoles(IEnumerable<SigningSubmitterRole> roles)
    {
        var ordered = roles
            .Select(Clone)
            .OrderBy(role => role.Order)
            .ThenBy(role => role.Name, StringComparer.Ordinal)
            .ToList();
        var allRoleUuids = ordered.Select(role => role.Uuid).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < ordered.Count; index++)
        {
            var role = ordered[index];
            role.Order = index;
            role.Name = string.IsNullOrWhiteSpace(role.Name)
                ? GetDefaultName(index)
                : role.Name;
            role.Color = NormalizeColor(role.Color, index);
            role.InviteByRoleUuid = NormalizeRoleReference(role, role.InviteByRoleUuid, allRoleUuids);
            role.OptionalInviteByRoleUuid = NormalizeRoleReference(role, role.OptionalInviteByRoleUuid, allRoleUuids);
            role.InviteViaFieldUuid = Fields.Any(field => string.Equals(field.Uuid, role.InviteViaFieldUuid, StringComparison.Ordinal))
                ? role.InviteViaFieldUuid
                : null;
        }

        return ordered;
    }

    private static SigningSubmitterRole Clone(SigningSubmitterRole role)
    {
        return new SigningSubmitterRole
        {
            Uuid = role.Uuid,
            Name = role.Name,
            Email = role.Email,
            Phone = role.Phone,
            FullName = role.FullName,
            Color = role.Color,
            Order = role.Order,
            IsRequester = role.IsRequester,
            IsOptional = role.IsOptional,
            InviteByRoleUuid = role.InviteByRoleUuid,
            OptionalInviteByRoleUuid = role.OptionalInviteByRoleUuid,
            InviteViaFieldUuid = role.InviteViaFieldUuid
        };
    }

    private static SigningSubmitterRole CreateDefaultRole(int index)
    {
        return new SigningSubmitterRole
        {
            Name = GetDefaultName(index),
            Color = SigningRolePalette.ColorForOrder(index),
            Order = index
        };
    }

    private static string GetDefaultName(int index)
    {
        return $"Signer {index + 1}";
    }

    private static string NormalizeColor(string? value, int index)
    {
        return string.IsNullOrWhiteSpace(value)
            ? SigningRolePalette.ColorForOrder(index)
            : value;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string? NormalizeRoleReference(SigningSubmitterRole role, string? value)
    {
        var allRoleUuids = _roles.Select(candidate => candidate.Uuid).ToHashSet(StringComparer.Ordinal);
        return NormalizeRoleReference(role, value, allRoleUuids);
    }

    private static string? NormalizeRoleReference(SigningSubmitterRole role, string? value, HashSet<string> allRoleUuids)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, role.Uuid, StringComparison.Ordinal)
            || !allRoleUuids.Contains(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static string GetFieldLabel(SigningField field)
    {
        if (!string.IsNullOrWhiteSpace(field.Name))
        {
            return field.Name;
        }

        if (!string.IsNullOrWhiteSpace(field.Title))
        {
            return field.Title;
        }

        return field.Uuid;
    }
}
