using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// The WASM demo's <see cref="IEmailPreviewRenderer"/> (contract §3.4): it posts the document to the demo
/// API and maps the answer back. This is the proof that the editor needs no Scriban, Mjml.Net or AngleSharp
/// in the browser — <c>Tempo.Blazor.EmailTemplates.Engine</c> is referenced by the API only.
/// </summary>
/// <remarks>
/// The document travels as the canonical <see cref="EmailTemplateSerializer"/> JSON (polymorphic blocks are
/// its job, not <c>System.Text.Json</c>'s); variables and culture are plain strings. A transport or server
/// failure becomes <see cref="EmailPreviewStatus.Unavailable"/> so the editor offers a retry, while a
/// cancelled token propagates — the editor cancels a superseded render and must not be told it failed.
/// </remarks>
public sealed class HttpEmailPreviewRenderer : IEmailPreviewRenderer
{
    private readonly IEmailTemplateApiClient _api;

    /// <summary>Initializes the renderer with the demo API client.</summary>
    /// <param name="api">The typed HTTP client of the demo email template API.</param>
    public HttpEmailPreviewRenderer(IEmailTemplateApiClient api) => _api = api;

    /// <inheritdoc />
    public async Task<EmailPreviewResult> RenderAsync(
        EmailPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RenderPreviewResponse response;
        try
        {
            response = await _api.PreviewAsync(new RenderPreviewRequest
            {
                ContentJson = EmailTemplateSerializer.Serialize(request.Document),
                VariablesJson = request.VariablesJson,
                SubstituteVariables = request.SubstituteVariables,
                CultureName = request.CultureName,
            }, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return EmailPreviewResult.Unavailable(
                [new RenderError(ex.Message, Key: RenderKeys.PreviewUnavailable)]);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A client-side timeout: the server never answered, which is exactly "unavailable".
            return EmailPreviewResult.Unavailable(
                [new RenderError("The preview request timed out.", Key: RenderKeys.PreviewUnavailable)]);
        }

        return new EmailPreviewResult(
            response.Html ?? string.Empty,
            response.Text ?? string.Empty,
            response.Subject ?? string.Empty,
            response.Preheader,
            response.Errors.Select(ToRenderError).ToList(),
            response.Status);
    }

    private static RenderError ToRenderError(RenderErrorDto dto)
        => new(dto.Message, dto.Line, dto.Column, dto.Key, dto.Severity);
}
