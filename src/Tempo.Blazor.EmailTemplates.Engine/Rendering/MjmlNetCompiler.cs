using Mjml.Net;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Default <see cref="IMjmlCompiler"/> backed by Mjml.Net. Validation errors are mapped to
/// <see cref="RenderError"/>s and parser exceptions (e.g. truncated MJML, see E0.9) are caught and
/// reported instead of propagating.
/// </summary>
public sealed class MjmlNetCompiler : IMjmlCompiler
{
    private static readonly MjmlRenderer Renderer = new();

    /// <inheritdoc />
    public MjmlCompileResult Compile(string mjml)
    {
        try
        {
            var result = Renderer.Render(mjml, new MjmlOptions { Beautify = false });
            var errors = (result.Errors ?? Enumerable.Empty<ValidationError>())
                .Select(e => new RenderError(e.Error, e.Position.LineNumber, e.Position.LinePosition))
                .ToList();
            return new MjmlCompileResult(result.Html ?? string.Empty, errors);
        }
        catch (Exception ex)
        {
            return new MjmlCompileResult(string.Empty, new[] { new RenderError(ex.Message) });
        }
    }
}
