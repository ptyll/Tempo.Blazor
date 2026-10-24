using Tempo.Blazor.Components.Wireframe;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>
/// Request-side validation of the stencil theme name the wireframe demo endpoints accept (<c>?theme=</c>, the export body's <c>Theme</c>):
/// at most 64 characters from <c>[A-Za-z0-9._-]</c> (<see cref="WireframeStencilThemeName"/>). A malformed name is a 400 and the raw
/// input is never echoed or logged; a well-formed but unknown name is fine (the default tokens are drawn).
/// </summary>
internal static class WireframeThemeRequest
{
    /// <summary>A 400 validation problem when <paramref name="theme"/> is malformed, otherwise <c>null</c>.</summary>
    public static IResult? Validate(string? theme)
        => WireframeStencilThemeName.IsValid(theme)
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["theme"] = [$"The theme must be at most {WireframeStencilThemeName.MaxLength} characters from A-Z, a-z, 0-9, '.', '_' and '-'."],
            });
}
