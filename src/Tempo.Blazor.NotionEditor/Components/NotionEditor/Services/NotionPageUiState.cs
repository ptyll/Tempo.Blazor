namespace Tempo.Blazor.Components.NotionEditor.Services;

/// <summary>
/// Mutable UI state shared between <see cref="Page.TmNotionPage"/> and the page chrome it renders
/// (the settings menu). Cascaded by name so programmatic open requests reach the menu without
/// lifting its whole state up.
/// </summary>
internal sealed class NotionPageUiState
{
    /// <summary>Raised when chrome (the mobile action bar) requests the page settings menu to open.</summary>
    public event Func<Task>? SettingsMenuRequested;

    /// <summary>Requests the page settings menu to open.</summary>
    public Task RequestSettingsMenuAsync()
        => SettingsMenuRequested?.Invoke() ?? Task.CompletedTask;
}
