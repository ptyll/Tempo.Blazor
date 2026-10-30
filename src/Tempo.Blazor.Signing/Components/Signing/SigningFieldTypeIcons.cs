using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// The shared field-type → icon map of the Signing package (Signing redesign phase 3, SI4). The
/// palette of <see cref="TmPdfTemplateDesigner"/>, the document overlays of
/// <see cref="TmSigningFieldOverlay"/> and the runner's field list all render the SAME icon for a
/// field type, so a signer recognises the field in the list, on the page and in the authoring
/// palette. The map keeps the overlay's names — the contract an E2E assertion pins
/// (Stamp → <c>shield</c>) — and the designer adopts it; every name resolves against the core
/// <c>TmIcon</c> built-ins.
/// </summary>
internal static class SigningFieldTypeIcons
{
    /// <summary>Gets the icon name for a signing field type.</summary>
    public static string Get(SigningFieldType type) => type switch
    {
        SigningFieldType.Signature or SigningFieldType.Initials => "edit",
        SigningFieldType.Date or SigningFieldType.DateNow => "calendar",
        SigningFieldType.Number => "hash",
        SigningFieldType.Image => "image",
        SigningFieldType.File => "file",
        SigningFieldType.Select or SigningFieldType.Multiple => "list",
        SigningFieldType.Checkbox => "check-square",
        SigningFieldType.Radio => "circle",
        SigningFieldType.Cells => "grid",
        SigningFieldType.Stamp => "shield",
        SigningFieldType.Phone => "phone",
        SigningFieldType.Verification or SigningFieldType.Kba => "lock",
        SigningFieldType.Payment => "tag",
        SigningFieldType.Heading => "type",
        SigningFieldType.Strikethrough => "minus",
        _ => "file-text"
    };
}
