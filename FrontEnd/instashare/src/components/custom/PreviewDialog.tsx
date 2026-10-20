import { useCallback, useEffect } from "react";
import { ChevronLeft, ChevronRight, Download, ExternalLink, X } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "../ui/dialog";
import { Button } from "../ui/button";
import FilePreview from "./FilePreview";
import FileIcon from "./FileIcon";
import type { Item } from "@/models/fileEntry";
import { formatBytes } from "@/utils/utils";
import { downloadUrl } from "@/utils/download";

type FileItem = Extract<Item, { type: 'file' }>;

/**
 * Full-window viewer. Arrow keys and the side buttons move between the previewable files of the folder.
 */
const PreviewDialog: React.FC<{
    items: FileItem[];
    index: number | null;
    onIndexChange: (index: number | null) => void;
}> = ({ items, index, onIndexChange }) => {
    const item = index !== null ? items[index] : undefined;
    const hasPrev = index !== null && index > 0;
    const hasNext = index !== null && index < items.length - 1;

    const go = useCallback((delta: number) => {
        if (index === null) return;
        const next = index + delta;
        if (next >= 0 && next < items.length) onIndexChange(next);
    }, [index, items.length, onIndexChange]);

    useEffect(() => {
        if (!item) return;
        const onKey = (e: KeyboardEvent) => {
            // Leave arrow keys alone inside players and text so seeking and scrolling still work.
            const target = e.target as HTMLElement;
            if (target.closest('video, audio, pre, .prose')) return;
            if (e.key === 'ArrowLeft') go(-1);
            if (e.key === 'ArrowRight') go(1);
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [item, go]);

    // The list refreshes while uploads finish; close if the file disappears.
    useEffect(() => {
        if (index !== null && !items[index]) onIndexChange(null);
    }, [index, items, onIndexChange]);

    return (
        <Dialog open={!!item} onOpenChange={(open) => !open && onIndexChange(null)}>
            <DialogContent
                className="flex h-[100dvh] w-screen max-w-none flex-col rounded-none sm:h-[92dvh] sm:w-[94vw] sm:max-w-6xl sm:rounded-xl overflow-hidden"
                onOpenAutoFocus={(e) => e.preventDefault()}
            >
                {item && (
                    <>
                        <header className="flex items-center gap-3 border-b px-3 py-2 sm:px-4">
                            <FileIcon kind={item.kind} />
                            <div className="min-w-0 flex-1">
                                <DialogTitle className="truncate text-sm font-medium sm:text-base" title={item.name}>{item.name}</DialogTitle>
                                <DialogDescription className="text-xs text-muted-foreground">
                                    {formatBytes(item.entry.size)} · {index! + 1} of {items.length}
                                </DialogDescription>
                            </div>
                            <Button variant="ghost" size="icon" asChild title="Open in new tab">
                                <a href={item.entry.url} target="_blank" rel="noopener noreferrer" aria-label="Open in new tab"><ExternalLink /></a>
                            </Button>
                            <Button variant="outline" size="sm" onClick={() => downloadUrl(item.entry.url, item.name)}>
                                <Download /> <span className="hidden sm:inline">Download</span>
                            </Button>
                            <Button variant="ghost" size="icon" onClick={() => onIndexChange(null)} aria-label="Close preview" title="Close (Esc)">
                                <X />
                            </Button>
                        </header>

                        <div className="relative flex min-h-0 flex-1 items-center justify-center bg-muted/40">
                            <FilePreview key={item.path} item={item} />

                            {hasPrev && (
                                <Button variant="secondary" size="icon" onClick={() => go(-1)} aria-label="Previous file" title="Previous (←)"
                                        className="absolute left-2 top-1/2 -translate-y-1/2 rounded-full shadow-md opacity-80 hover:opacity-100">
                                    <ChevronLeft />
                                </Button>
                            )}
                            {hasNext && (
                                <Button variant="secondary" size="icon" onClick={() => go(1)} aria-label="Next file" title="Next (→)"
                                        className="absolute right-2 top-1/2 -translate-y-1/2 rounded-full shadow-md opacity-80 hover:opacity-100">
                                    <ChevronRight />
                                </Button>
                            )}
                        </div>
                    </>
                )}
            </DialogContent>
        </Dialog>
    );
};

export default PreviewDialog;
