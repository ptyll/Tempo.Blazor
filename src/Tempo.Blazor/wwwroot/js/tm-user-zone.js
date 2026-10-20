// Resolves the browser's IANA time zone id once per Blazor Server circuit.
// Called by TmUserTimeZone.EnsureResolvedAsync after the first interactive render;
// an unknown/unavailable value is handled on the .NET side (UTC fallback).
export function resolve() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
    } catch {
        return null;
    }
}
