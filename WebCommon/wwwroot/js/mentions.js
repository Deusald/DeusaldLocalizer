// ── @mention autocomplete bridge ─────────────────────────────────
// Small helpers for the MentionTextArea component: read/set the caret of a textarea, and forward the
// navigation keys (arrows / Enter / Tab / Escape) to .NET *synchronously* preventing their default only
// while the suggestion popup is open — Blazor's declarative preventDefault can't be made per-key, and
// preventDefault must run in the handler before any await, so it lives here.

const _states = new Map(); // element -> { open, handler, ref }

export function attach(element, dotNetRef) {
    detach(element);

    const state = { open: false, ref: dotNetRef, handler: null };

    state.handler = (e) => {
        if (!state.open) return;
        const k = e.key;
        if (k === 'ArrowDown' || k === 'ArrowUp' || k === 'Enter' || k === 'Tab' || k === 'Escape') {
            e.preventDefault();
            state.ref.invokeMethodAsync('OnNavKey', k);
        }
    };

    element.addEventListener('keydown', state.handler);
    _states.set(element, state);
}

export function detach(element) {
    const state = _states.get(element);
    if (state && state.handler) {
        element.removeEventListener('keydown', state.handler);
    }
    _states.delete(element);
}

export function setOpen(element, open) {
    const state = _states.get(element);
    if (state) state.open = open;
}

export function caret(element) {
    return element ? (element.selectionStart ?? 0) : 0;
}

export function setCaret(element, index) {
    if (!element) return;
    element.focus();
    try { element.setSelectionRange(index, index); } catch { /* detached */ }
}
