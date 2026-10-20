// Browser-only; kept apart from utils.ts so the Pages Function can import that.

/** A browser download that keeps the original file name. The CDN sends Content-Disposition, so this never navigates away. */
export function downloadUrl(url: string, fileName: string) {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    a.remove();
}
