using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared Notion-editor interactions for the lanes that type into the demo editor. One copy of
/// the caret-block centering the slash-menu specs need (CF3's park-at-the-edge workaround).
/// </summary>
internal static class NotionEditorHelper
{
    /// <summary>
    /// Scrolls the focused caret block to the viewport centre. Enter can leave the caret block
    /// flush with an editor viewport edge; a slash anchor even a few pixels below the fold is
    /// "fully out of the viewport" for overlay.js, which then PARKS the panel
    /// (visibility:hidden) until something scrolls — nothing does, so the menu never shows.
    /// scrollIntoView fixes EVERY scrollable ancestor (the editor's own scroller or the
    /// window), so the caret lands mid-viewport whatever grew the page.
    /// <para>
    /// Returns TRUE when the focused element really was a Notion paragraph block. The tests
    /// assert on it: a false result means the caret is somewhere else entirely and the
    /// pre-typing state is wrong — failing here is loud, while the uncentred park would only
    /// flake later on the slash menu.
    /// </para>
    /// </summary>
    public static async Task<bool> CenterFocusedParagraphAsync(IPage page)
        => await page.EvaluateAsync<bool>(@"() => {
            const active = document.activeElement;
            if (!active || !active.classList || !active.classList.contains('tm-notion-paragraph')) return false;
            active.scrollIntoView({ block: 'center', behavior: 'instant' });
            return true;
        }");
}
