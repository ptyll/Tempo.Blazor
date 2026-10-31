// Tempo.Blazor.AI — message interop.
//
// TmAiMessage needs the browser for exactly three things, and every one of them is feature-detected so a
// host in a restricted environment (no clipboard permission, no speech synthesis, a stripped module)
// simply gets a quieter component instead of a dead button:
//
//   copyText(text)            -> navigator.clipboard.writeText, true when the write went through
//   isSpeechSupported()       -> whether the browser exposes speechSynthesis + SpeechSynthesisUtterance
//   speak(text, lang, ref)    -> cancel the previous utterance and speak; invokes OnSpeechEndedAsync
//   stopSpeaking()            -> cancel the current utterance
//   focusCitation(root, id)   -> focus the citation chip that opened the source preview
//
// The module is a plain ES module (no eval, no inline handlers), imported through IJSObjectReference, so a
// host Content-Security-Policy needs no 'unsafe-inline' for it. It attaches no DOM listener, so there is
// nothing to detach: the component releasing its object reference is enough.

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text ?? '');
        return true;
    } catch {
        // Refused write (permissions, insecure context): the caller keeps its uncopied state rather than
        // claiming a success that did not happen.
        return false;
    }
}

export function isSpeechSupported() {
    return typeof globalThis.speechSynthesis !== 'undefined'
        && typeof globalThis.SpeechSynthesisUtterance === 'function';
}

export function speak(text, lang, dotNetRef) {
    if (!isSpeechSupported()) {
        return false;
    }

    const utterance = new SpeechSynthesisUtterance(text ?? '');
    if (lang) {
        utterance.lang = lang;
    }

    // The end of the utterance is reported back so the toggle returns to "Read aloud" by itself; a
    // component that only tracked its own press would keep offering "Stop reading" after the voice
    // finished.
    if (dotNetRef) {
        utterance.onend = () => {
            try {
                dotNetRef.invokeMethodAsync('OnSpeechEndedAsync')?.catch(() => {});
            } catch {
                // The circuit is gone; there is nobody left to update.
            }
        };
    }

    globalThis.speechSynthesis.cancel();
    globalThis.speechSynthesis.speak(utterance);
    return true;
}

export function stopSpeaking() {
    if (isSpeechSupported()) {
        globalThis.speechSynthesis.cancel();
    }
}

// Restores focus to the citation chip that opened the source preview. The chip is produced by the
// markdown module's generated HTML, so Blazor holds no ElementReference for it, and the preview's
// overlay anchor is the message body — a plain div that cannot take focus. The chip is looked up by
// its stable data-source-id (compared as an attribute value, never interpolated into a selector).
export function focusCitation(root, sourceId) {
    if (!root || !sourceId) {
        return false;
    }

    for (const chip of root.querySelectorAll('[data-source-id]')) {
        if (chip.getAttribute('data-source-id') === sourceId && typeof chip.focus === 'function') {
            chip.focus();
            return true;
        }
    }

    return false;
}

