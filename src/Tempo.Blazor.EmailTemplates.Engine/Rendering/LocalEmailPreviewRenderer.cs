using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The default <see cref="IEmailPreviewRenderer"/> (§3.4): renders locally through
/// <see cref="IEmailTemplateRenderer"/> and projects the result onto the preview contract.
/// Registered by <c>AddTempoEmailTemplateEngine()</c>; a host that renders on another machine registers
/// its own (for example an HTTP client) and the editor does not care which one it got.
/// </summary>
/// <remarks>
/// <para>
/// Mapping rules (D17/D23): warnings keep <see cref="EmailPreviewStatus.Rendered"/> — the HTML is complete
/// and the editor shows the warnings separately; a real
/// <see cref="RenderErrorSeverity.Error"/> is <see cref="EmailPreviewStatus.RenderFailed"/> and empties
/// html/text/subject; an unexpected failure of the renderer itself is
/// <see cref="EmailPreviewStatus.Unavailable"/> so the editor offers a retry instead of blaming the
/// template. <see cref="OperationCanceledException"/> is never swallowed — the editor uses it to cancel a
/// superseded render.
/// </para>
/// <para>
/// <see cref="EmailPreviewRequest.SubstituteVariables"/> = <see langword="false"/> renders the same pipeline
/// without substituting, so the output keeps its <c>{{ … }}</c> tokens (HTML export). A host-supplied
/// <see cref="IEmailTemplateRenderer"/> that is not Tempo's own pipeline cannot honour that switch, and
/// substituting anyway would return an export whose tokens are already resolved — silently wrong. The
/// request is therefore answered with <see cref="EmailPreviewStatus.RenderFailed"/> and
/// <see cref="RenderKeys.PreviewUnavailable"/> ("this renderer cannot keep tokens"), never with substituted
/// HTML the host would download believing it is a template.
/// </para>
/// </remarks>
public sealed class LocalEmailPreviewRenderer : IEmailPreviewRenderer
{
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger<LocalEmailPreviewRenderer>? _logger;

    /// <summary>Initializes the renderer with the render pipeline it wraps.</summary>
    /// <param name="renderer">The render pipeline.</param>
    /// <param name="logger">Logger for the documented custom-renderer degradation.</param>
    public LocalEmailPreviewRenderer(
        IEmailTemplateRenderer renderer,
        ILogger<LocalEmailPreviewRenderer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        _renderer = renderer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EmailPreviewResult> RenderAsync(
        EmailPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = BuildOptions(request);
        try
        {
            RenderResult result;
            if (!request.SubstituteVariables)
            {
                if (_renderer is EmailTemplateRenderer local)
                {
                    result = await local.RenderWithoutSubstitutionAsync(request.Document, options, cancellationToken);
                }
                else
                {
                    _logger?.LogWarning(
                        "A custom IEmailTemplateRenderer cannot keep template tokens; refusing the no-substitution render.");
                    return new EmailPreviewResult(
                        string.Empty, string.Empty, string.Empty, null,
                        [new RenderError(
                            "The registered renderer cannot render without substituting template variables.",
                            Key: RenderKeys.PreviewUnavailable)],
                        EmailPreviewStatus.RenderFailed);
                }
            }
            else
            {
                result = await _renderer.RenderAsync(
                    request.Document, ParseVariables(request.VariablesJson), options, cancellationToken);
            }

            return Project(result);
        }
        catch (OperationCanceledException)
        {
            // The editor cancels a superseded render; that is not a preview failure.
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The preview renderer failed; reporting the preview as unavailable.");
            return EmailPreviewResult.Unavailable(
                [new RenderError(ex.Message, Key: RenderKeys.PreviewUnavailable)]);
        }
    }

    private static EmailPreviewResult Project(RenderResult result)
    {
        if (!result.Success)
        {
            // Failed => html/text/subject are "" — never null (D17).
            return new EmailPreviewResult(
                string.Empty, string.Empty, string.Empty, null, result.Errors, EmailPreviewStatus.RenderFailed);
        }

        // Warnings (blocked URL, removed event handler) keep the render successful (D23).
        return new EmailPreviewResult(
            result.Html,
            result.TextVersion,
            result.Subject,
            result.Preheader,
            result.Errors,
            EmailPreviewStatus.Rendered);
    }

    private static TemplateRenderOptions? BuildOptions(EmailPreviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CultureName))
        {
            return null;
        }

        try
        {
            return new TemplateRenderOptions { Culture = CultureInfo.GetCultureInfo(request.CultureName) };
        }
        catch (ArgumentException)
        {
            // An unknown culture name is not a render failure: fall back to the renderer default.
            return null;
        }
    }

    private static object? ParseVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(variablesJson);
        }
        catch (JsonException)
        {
            // Invalid JSON was validated by the editor already; render with no data instead of failing.
            return null;
        }
    }
}
