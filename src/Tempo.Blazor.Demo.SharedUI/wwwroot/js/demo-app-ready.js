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
