import { Download, Maximize2 } from "lucide-react";
import { Button } from "../ui/button";
import FileIcon from "./FileIcon";
import FilePreview from "./FilePreview";
import StatusBadge from "./StatusBadge";
import type { Item } from "@/models/fileEntry";
import { formatBytes, isPreviewable } from "@/utils/utils";
import { downloadUrl } from "@/utils/download";

type FileItem = Extract<Item, { type: 'file' }>;

/** A share of one file: show it big, with one obvious button. */
const SingleFileView: React.FC<{ item: FileItem; onExpand: () => void }> = ({ item, onExpand }) => {
    const ready = item.entry.status === 'uploaded';
    const canPreview = ready && isPreviewable(item.kind);

    return (
        <section className="overflow-hidden rounded-xl border bg-card">
            {canPreview && (
                <div className="relative flex h-[min(65dvh,640px)] items-center justify-center bg-muted/40">
                    <FilePreview item={item} />
                    <Button variant="secondary" size="icon" onClick={onExpand} aria-label="Full screen" title="Full screen"
                            className="absolute right-3 top-3 rounded-full shadow-md opacity-80 hover:opacity-100">
                        <Maximize2 />
                    </Button>
                </div>
            )}
            <div className="flex flex-col items-center gap-4 p-6 text-center sm:flex-row sm:text-left">
                <FileIcon kind={item.kind} className="size-10" />
                <div className="min-w-0 flex-1">
                    <p className="break-all font-medium">{item.name}</p>
                    <div className="mt-1 flex items-center justify-center gap-2 text-sm text-muted-foreground sm:justify-start">
                        <span>{formatBytes(item.entry.size)}</span>
                        <StatusBadge status={item.entry.status} label={item.entry.status === 'uploading' ? 'Still uploading, this page updates by itself' : undefined} />
                    </div>
                </div>
                <Button size="lg" disabled={!ready} onClick={() => downloadUrl(item.entry.url, item.name)} className="w-full sm:w-auto">
                    <Download /> Download
                </Button>
            </div>
        </section>
    );
};

export default SingleFileView;
