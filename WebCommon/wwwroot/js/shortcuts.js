// ── Editor keyboard shortcuts ────────────────────────────────────
// Bridges document-level Undo/Redo shortcuts to a .NET component. A single listener is kept at module
// scope (only one top bar exists at a time); register() replaces any previous listener and unregister()
// removes it, both called from the component's lifecycle.

let _handler = null;

export function register(dotNetRef) {
    unregister();

    _handler = (e) => {
        // Let the browser's native text undo/redo win while the user is typing in an editable field —
        // only hijack Ctrl/Cmd+Z / Ctrl/Cmd+Y when focus is outside inputs.
        const t   = e.target;
        const tag = t && t.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || (t && t.isContentEditable)) return;

        if (!(e.ctrlKey || e.metaKey)) return;

        const key = e.key.toLowerCase();
        if (key === 'z' && !e.shiftKey) {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('Undo');
        } else if (key === 'y' || (key === 'z' && e.shiftKey)) {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('Redo');
        }
    };

    document.addEventListener('keydown', _handler);
}

export function unregister() {
    if (_handler) {
        document.removeEventListener('keydown', _handler);
        _handler = null;
    }
}
