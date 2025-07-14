export const formatBytes = (bytes: number = 0) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};


export function smartCompare(a: string, b: string) {
    const isDigitA = /^\d+/.test(a);
    const isDigitB = /^\d+/.test(b);
    if (isDigitA && isDigitB) {
        // Compare numerically
        const numA = parseInt(a.match(/^\d+/)?.[0] || '0', 10);
        const numB = parseInt(b.match(/^\d+/)?.[0] || '0', 10);
        if (numA !== numB) return numA - numB;
        // If numbers are equal, fallback to string compare
        return a.localeCompare(b);
    }
    // If only one starts with digit, sort digits first
    if (isDigitA && !isDigitB) return -1;
    if (!isDigitA && isDigitB) return 1;
    // Otherwise, string compare
    return a.localeCompare(b);
}