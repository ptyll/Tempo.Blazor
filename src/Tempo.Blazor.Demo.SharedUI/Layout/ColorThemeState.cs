namespace Tempo.Blazor.Demo.SharedUI.Layout;

/// <summary>
/// The demo's colour theme, shared by every <c>ColorThemeSwitch</c> in the layout. The layout
/// renders two of them (sidebar and mobile header) and they must agree, so the choice lives here
/// rather than in each component. Scoped: one per circuit or tab, which is one per page.
/// </summary>
public sealed class ColorThemeState
{
    public const string StorageKey = "tm-demo-color-theme";
    public const string Default = "default";
    public const string Indigo = "indigo";

    public string Current { get; private set; } = Default;

    public event Action? Changed;

    public void Set(string theme)
    {
        if (theme == Current)
        {
            return;
        }

        Current = theme;
        Changed?.Invoke();
    }
}
