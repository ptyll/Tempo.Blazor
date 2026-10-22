using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Dtos;

/// <summary>Maps between the editable <see cref="EmailTemplateDocument"/> and transport DTOs.</summary>
public static class EmailTemplateMapper
{
    /// <summary>Serializes a document to its canonical content JSON.</summary>
    public static string ToContentJson(EmailTemplateDocument document) => EmailTemplateSerializer.Serialize(document);

    /// <summary>Deserializes a document from content JSON.</summary>
    public static EmailTemplateDocument ToDocument(string contentJson) => EmailTemplateSerializer.Deserialize(contentJson);

    /// <summary>
    /// Deserializes a document from content JSON with explicit import limits (Fáze 10, carry-forward):
    /// the server path applies the same <see cref="EmailImportOptions"/> as the editor, so “what passes
    /// the import passes the server” also holds for JSON sent by a client.
    /// </summary>
    /// <param name="contentJson">The canonical content JSON.</param>
    /// <param name="options">The limits to apply; <see langword="null"/> keeps the historical behaviour (no limits).</param>
    /// <returns>The deserialized document.</returns>
    /// <exception cref="EmailTemplateSerializationException">
    /// The payload is too large, malformed, written by a newer schema, or violates a document limit; its
    /// <see cref="EmailTemplateSerializationException.Key"/> names the localization key a host turns into a
    /// message (a 422 response, never a 500).
    /// </exception>
    public static EmailTemplateDocument ToDocument(string contentJson, EmailImportOptions? options)
        => EmailTemplateSerializer.Deserialize(contentJson, options);

    /// <summary>Computes the distinct root variable names a document requires.</summary>
    public static IReadOnlyList<string> RequiredVariables(EmailTemplateDocument document)
        => EmailDocumentVariableExtractor.Extract(document)
            .Select(v => v.Path.Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    /// <summary>Builds the full detail DTO from a document.</summary>
    public static EmailTemplateDetailDto ToDetailDto(EmailTemplateDocument document, bool isActive = true, string? sampleDataJson = null)
        => new()
        {
            Id = document.Id,
            Name = document.Name,
            Subject = document.Subject,
            Preheader = document.Preheader,
            Language = document.Language,
            ContentJson = ToContentJson(document),
            RequiredVariables = RequiredVariables(document),
            SampleDataJson = sampleDataJson,
            IsActive = isActive,
            UpdatedAt = document.UpdatedAt,
        };

    /// <summary>Builds the list summary DTO from a document.</summary>
    public static EmailTemplateSummaryDto ToSummaryDto(EmailTemplateDocument document, bool isActive = true)
        => new()
        {
            Id = document.Id,
            Name = document.Name,
            Subject = document.Subject,
            Language = document.Language,
            IsActive = isActive,
            UpdatedAt = document.UpdatedAt,
        };

    /// <summary>Builds a new document from a create request (content + overridden metadata).</summary>
    public static EmailTemplateDocument ApplyCreate(CreateEmailTemplateRequest request)
        => ApplyCreate(request, null);

    /// <summary>
    /// Builds a new document from a create request with explicit import limits, so a create endpoint can
    /// refuse an oversized or too-deeply nested payload with a localizable key instead of a 500.
    /// </summary>
    /// <param name="request">The create request.</param>
    /// <param name="options">The limits to apply; <see langword="null"/> keeps the historical behaviour.</param>
    /// <returns>The document the request describes.</returns>
    /// <exception cref="EmailTemplateSerializationException">The content JSON violates a limit.</exception>
    public static EmailTemplateDocument ApplyCreate(CreateEmailTemplateRequest request, EmailImportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = ToDocument(request.ContentJson, options);
        document.Name = request.Name;
        document.Subject = request.Subject;
        document.Preheader = request.Preheader;
        document.Language = request.Language;
        return document;
    }

    /// <summary>Applies an update request's content and metadata onto a document.</summary>
    public static EmailTemplateDocument ApplyUpdate(UpdateEmailTemplateRequest request)
        => ApplyUpdate(request, null);

    /// <summary>
    /// Applies an update request's content and metadata onto a document with explicit import limits
    /// (same invariant as <see cref="ApplyCreate(CreateEmailTemplateRequest, EmailImportOptions?)"/>).
    /// </summary>
    /// <param name="request">The update request.</param>
    /// <param name="options">The limits to apply; <see langword="null"/> keeps the historical behaviour.</param>
    /// <returns>The document the request describes.</returns>
    /// <exception cref="EmailTemplateSerializationException">The content JSON violates a limit.</exception>
    public static EmailTemplateDocument ApplyUpdate(UpdateEmailTemplateRequest request, EmailImportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = ToDocument(request.ContentJson, options);
        document.Name = request.Name;
        document.Subject = request.Subject;
        document.Preheader = request.Preheader;
        document.Language = request.Language;
        document.UpdatedAt = DateTime.UtcNow;
        return document;
    }
}
