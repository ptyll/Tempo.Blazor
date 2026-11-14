using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>Shared helpers for the feature E2E classes: repository-root resolution and scoped axe-core scans.</summary>
internal static class TestContextHelper
{
    private static readonly Lazy<string> Root = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    });

    /// <summary>Absolute path of the repository root (the folder containing TempoBlazor.slnx).</summary>
    public static string RepositoryRoot => Root.Value;
}

/// <summary>
/// Scoped axe-core (WCAG 2.1 A/AA) scans, mirroring <see cref="AriaWiringE2ETests"/>: the script is
/// injected from the CDN, the run is limited to a host selector, and only critical/serious
/// violations are returned.
/// </summary>
internal static class AxeScan
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    public static async Task<string[]> RunAsync(IPage page, string hostSelector)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        return await page.EvaluateAsync<string[]>(
            """
            async (hostSelector) => {
                const host = document.querySelector(hostSelector) || document.body;
                // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                // like the skeleton shimmer, never finish and are not waited for).
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """, hostSelector);
    }
}
