using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Accessibility;

/// <summary>
/// The shared computed-style contrast probe for the KG-008 audit (plan 92e96b25 phase 11) and the
/// diagram node-text assertion (<c>TmDiagramThemeE2ETests</c>). It composites the element's whole
/// background chain — every translucent layer from the outermost ancestor down to the element
/// itself is alpha-blended in order, which is exactly what the eye sees for e.g. a translucent
/// chip wash over a disabled box — then computes WCAG contrast. The arithmetic mirrors the bUnit
/// <c>ThemeCss.PaintedRatio</c> guard, so a number measured here can be compared with the number
/// the unit suite pins.
/// </summary>
internal static class ContrastProbe
{
    /// <summary>
    /// JS evaluated on the measured element; returns the ratio plus both colours for the message.
    /// </summary>
    public const string MeasureJs = @"el => {
        const parse = c => {
            const m = c && c.match(/rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)/);
            return m ? [+m[1], +m[2], +m[3], m[4] === undefined ? 1 : +m[4]] : null;
        };
        const lum = ([r, g, b]) => {
            const f = v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
        };
        const chain = [];
        let n = el;
        while (n && n.nodeType === 1) {
            const bg = parse(getComputedStyle(n).backgroundColor);
            if (bg) chain.push(bg);
            n = n.parentElement;
        }
        let acc = chain.length ? chain[chain.length - 1].slice() : [255, 255, 255, 1];
        for (let i = chain.length - 2; i >= 0; i--) {
            const [r, g, b, a] = chain[i];
            acc = [a * r + (1 - a) * acc[0], a * g + (1 - a) * acc[1], a * b + (1 - a) * acc[2], 1];
        }
        const fg = parse(getComputedStyle(el).color);
        const ratio = (Math.max(lum(fg), lum(acc)) + 0.05) / (Math.min(lum(fg), lum(acc)) + 0.05);
        return { ratio, fg: fg.slice(0, 3).map(Math.round), bg: acc.slice(0, 3).map(Math.round) };
    }";

    public sealed record Result
    {
        public double Ratio { get; init; }
        public int[] Fg { get; init; } = [];
        public int[] Bg { get; init; } = [];
    }

    /// <summary>Waits for the element, measures it and asserts the WCAG AA 4.5:1 floor.</summary>
    public static async Task<double> RatioAsync(ILocator element, string label)
    {
        await element.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        var result = await element.EvaluateAsync<Result>(MeasureJs);
        Assert.IsTrue(
            result.Ratio >= 4.5,
            $"{label}: text rgb({string.Join(", ", result.Fg)}) on composited " +
            $"rgb({string.Join(", ", result.Bg)}) = {result.Ratio:0.00}:1, under the 4.5:1 WCAG AA floor");
        return result.Ratio;
    }
}
