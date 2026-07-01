// ── Drag-and-drop helper exposed to Blazor ───────────────────────
window.registerDropZone = (element, dotnetRef) => {
    if (!element) return;

    element.addEventListener('dragover', e => {
        e.preventDefault();
        element.classList.add('dragover');
    });
    element.addEventListener('dragleave', () => {
        element.classList.remove('dragover');
    });
    element.addEventListener('drop', async e => {
        e.preventDefault();
        element.classList.remove('dragover');
        const file = e.dataTransfer?.files?.[0];
        if (file) await dotnetRef.invokeMethodAsync('OnFileDrop', file.path ?? file.name);
    });
};