// Tempo.Blazor.AI — markdown view interop.
//
// The markdown itself is rendered on the server (AiMarkdownRenderer), so a citation chip can land inside
// any inline element; a Blazor component cannot be placed there. Those two interactions are therefore
// delegated from the component's root element:
//
//   [data-source-id]  a citation chip  -> dotNetRef.invokeMethodAsync('HandleCitationClickAsync', id)
//   [data-copy-code]  a code-block copy button -> navigator.clipboard.writeText(<pre><code> text)
//
// The module is a plain ES module (no eval, no inline handlers), imported through IJSObjectReference, so
// a host Content-Security-Policy needs no 'unsafe-inline' for it. One listener per root, captured on the
// element itself and removed by detach().

const COPY_FEEDBACK_MS = 2000;

export function attach(root, dotNetRef) {
    if (!root || root.__tmMarkdownView) {
        return;
    }

    const onClick = async event => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target || !root.contains(target)) {
            return;
        }

        const citation = target.closest('[data-source-id]');
        if (citation && root.contains(citation)) {
            event.preventDefault();
            const sourceId = citation.getAttribute('data-source-id');
            if (sourceId && dotNetRef) {
                await dotNetRef.invokeMethodAsync('HandleCitationClickAsync', sourceId);
            }
            return;
        }

        const copy = target.closest('[data-copy-code]');
        if (copy && root.contains(copy)) {
            event.preventDefault();
            await copyCode(copy);
        }
    };

    root.addEventListener('click', onClick);
    root.__tmMarkdownView = { onClick, timers: new Set() };
}

export function detach(root) {
    const state = root && root.__tmMarkdownView;
    if (!state) {
        return;
    }

    root.removeEventListener('click', state.onClick);
    for (const timer of state.timers) {
        clearTimeout(timer);
    }
    state.timers.clear();
    delete root.__tmMarkdownView;
}

async function copyCode(button) {
    const block = button.closest('.tm-markdown-view__code');
    const code = block ? block.querySelector('pre code') : null;
    if (!code) {
        return;
    }

    try {
        await navigator.clipboard.writeText(code.textContent ?? '');
    } catch {
        // A refused clipboard write (permissions, insecure context) leaves the button unchanged: there is
        // nothing to confirm to the user.
        return;
    }

    const root = button.closest('.tm-markdown-view');
    // The live region is the button's SIBLING in the code header row: a role="status" nested inside the
    // control is unreliable in assistive technology and would also join the button's accessible name.
    const status = button.parentElement
        ? button.parentElement.querySelector('[role="status"]')
        : null;
    const copiedLabel = button.getAttribute('data-copied-label') || '';
    const timers = root?.__tmMarkdownView?.timers;

    // The icon swap comes from the data attribute, and the copied confirmation is ANNOUNCED through the
    // live region — never by renaming the button. An accessible name that changes for two seconds makes the
    // control hard to find again, and the live region already carries the state change.
    button.setAttribute('data-copied', 'true');
    if (status) {
        status.textContent = copiedLabel;
    }

    if (!timers) {
        return;
    }

    const timer = setTimeout(() => {
        button.removeAttribute('data-copied');
        if (status) {
            status.textContent = '';
        }
        timers.delete(timer);
    }, COPY_FEEDBACK_MS);
    timers.add(timer);
}
