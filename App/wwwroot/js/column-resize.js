// Drives the draggable divider between the keys column and the key-detail column.
// Reads the target element's current width on pointer-down, then updates a global
// CSS variable (--kc-width) on every pointer-move so the resize stays smooth without
// round-tripping through Blazor. No state is written back to .NET.
window.columnResize = {
    start: function (selector, clientX, min, max) {
        const el = document.querySelector(selector);
        if (!el) return;

        const startWidth = el.getBoundingClientRect().width;

        const clamp = (x) => Math.max(min, Math.min(max, startWidth + (x - clientX)));

        const onMove = (e) => {
            document.documentElement.style.setProperty('--kc-width', clamp(e.clientX) + 'px');
        };

        const onUp = (e) => {
            document.removeEventListener('pointermove', onMove);
            document.removeEventListener('pointerup', onUp);
            document.body.style.userSelect = '';
            document.body.style.cursor = '';
            document.documentElement.style.setProperty('--kc-width', clamp(e.clientX) + 'px');
        };

        document.body.style.userSelect = 'none';
        document.body.style.cursor = 'col-resize';
        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp);
    }
};
