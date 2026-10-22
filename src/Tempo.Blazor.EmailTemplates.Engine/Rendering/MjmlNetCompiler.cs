using Mjml.Net;
using Mjml.Net.Helpers;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Default <see cref="IMjmlCompiler"/> backed by Mjml.Net. Validation errors are mapped to
/// <see cref="RenderError"/>s and parser exceptions (e.g. truncated MJML, see E0.9) are caught and
/// reported instead of propagating.
/// </summary>
public sealed class MjmlNetCompiler : IMjmlCompiler
{
    private static readonly MjmlRenderer Renderer = new();

    /// <summary>
    /// The compiler's options with its IMPLICIT font table removed (Fáze 10 security re-review, item 4).
    /// Mjml.Net ships a default <c>Ubuntu</c> font and writes an <c>@import url(https://fonts.googleapis.com/…)</c>
    /// for it into EVERY compiled message — a third-party fetch no template asked for, which the CSS rule of the
    /// markup guard would then have to keep exempting. The pipeline imports exactly the fonts the model carries
    /// (<c>mj-font</c>, emitted from an allow-listed href), so the default table stays empty and the only
    /// <c>@import</c>s in the output are the ones the author's own <c>mj-font</c> entries produced. A fresh
    /// instance per compile keeps the dictionary out of reach of concurrent renders.
    /// </summary>
    private static MjmlOptions CreateOptions()
        => new()
        {
            Beautify = false,
            Fonts = new Dictionary<string, Font>(),
        };

    /// <inheritdoc />
    public MjmlCompileResult Compile(string mjml)
    {
        try
        {
            var result = Renderer.Render(mjml, CreateOptions());
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
