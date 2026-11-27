/**
 * Demo shell helper: marks the body with the E2E readiness attribute
 * (data-blazor-ready) once an interactive runtime has rendered.
 *
 * Ships as an ES module so the demo shell stays CSP-safe: the former
 * `InvokeVoidAsync("eval", …)` call threw on the ?csp=1 demo variant, whose
 * Content-Security-Policy deliberately forbids `unsafe-eval`.
 */
export function markBodyReady() {
  document.body.setAttribute('data-blazor-ready', 'true');
}

/**
 * Deep-link helper: once the initial layout settles (fonts ready, the target's
 * document position stable for a few frames), focuses the target WITHOUT the
 * browser's own focus scroll and brings it into view with an INSTANT scroll.
 * The global `scroll-behavior: smooth` would turn a plain focus()/scrollIntoView()
 * into an animation that late-rendering sections above the target interrupt
 * mid-flight, leaving the target outside the viewport. The target needs
 * tabindex="-1" to be focusable (no tab stop is added).
 */
export async function focusDeepLinkTargetWhenSettled(selector) {
  const frame = () => new Promise(resolve => requestAnimationFrame(() => resolve()));
  const deadline = performance.now() + 10000;
  let previous = null;
  let stable = 0;
  await document.fonts.ready;
  while (performance.now() < deadline && stable < 3) {
    await frame();
    const el = document.querySelector(selector);
    if (!el) {
      previous = null;
      stable = 0;
      continue;
    }
    const r = el.getBoundingClientRect();
    const current = `${r.top + window.scrollY},${r.height}`;
    stable = current === previous ? stable + 1 : 0;
    previous = current;
  }
  const target = document.querySelector(selector);
  if (!target) return;
  target.focus({ preventScroll: true });
  target.scrollIntoView({ block: 'start', behavior: 'instant' });
}
