// Browser file pick / save for the Excel import/export modals. Bytes cross the interop boundary as
// base64 strings, which the C# side converts to/from a Stream.

export function pickXlsx() {
    return new Promise((resolve) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.xlsx';
        input.onchange = async () => {
            const file = input.files && input.files[0];
            if (!file) { resolve(null); return; }
            const buf = new Uint8Array(await file.arrayBuffer());
            let binary = '';
            for (let i = 0; i < buf.length; i++) binary += String.fromCharCode(buf[i]);
            resolve(btoa(binary));
        };
        // Some browsers fire cancel; treat any dismissal without a file as null.
        input.oncancel = () => resolve(null);
        input.click();
    });
}

export function saveBytes(fileName, base64, mimeType) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
