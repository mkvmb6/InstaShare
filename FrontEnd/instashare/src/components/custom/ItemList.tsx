import { Link } from "react-router-dom";
import { Download, Eye } from "lucide-react";
import { Button } from "../ui/button";
import { Checkbox } from "../ui/checkbox";
import FileIcon from "./FileIcon";
import StatusBadge from "./StatusBadge";
import type { Item } from "@/models/fileEntry";
import { formatBytes, isPreviewable } from "@/utils/utils";
import { downloadUrl } from "@/utils/download";
import { cn } from "@/lib/utils";

type FileItem = Extract<Item, { type: 'file' }>;

export interface ItemListProps {
    items: Item[];
    view: 'list' | 'grid';
    folderHref: (path: string) => string;
    selected: Set<string>;
    onToggle: (path: string) => void;
    onPreview: (item: FileItem) => void;
}

const ItemList: React.FC<ItemListProps> = ({ items, view, ...rest }) =>
    view === 'grid' ? (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
            {items.map((item) => <li key={item.path}><GridItem item={item} {...rest} /></li>)}
        </ul>
    ) : (
        <ul className="divide-y rounded-xl border bg-card">
            {items.map((item) => <li key={item.path}><ListRow item={item} {...rest} /></li>)}
        </ul>
    );

type RowProps = Omit<ItemListProps, 'items' | 'view'> & { item: Item };

const ListRow: React.FC<RowProps> = ({ item, folderHref, selected, onToggle, onPreview }) => {
    if (item.type === 'folder')
        return (
            <Link to={folderHref(item.path)} className="flex items-center gap-3 px-3 py-3 hover:bg-accent/60 focus-visible:bg-accent/60 outline-none sm:px-4">
                <span className="size-4 shrink-0" aria-hidden />
                <FileIcon kind="folder" />
                <span className="min-w-0 flex-1 truncate font-medium">{item.name}</span>
                <FolderMeta item={item} />
            </Link>
        );

    const ready = item.entry.status === 'uploaded';
    const canPreview = ready && isPreviewable(item.kind);
    return (
        <div className="flex items-center gap-3 px-3 py-2.5 sm:px-4">
            <Checkbox checked={selected.has(item.path)} onCheckedChange={() => onToggle(item.path)} disabled={!ready}
                      aria-label={`Select ${item.name}`} />
            <FileIcon kind={item.kind} />
            <div className="min-w-0 flex-1">
                {canPreview ? (
                    <button type="button" onClick={() => onPreview(item)} className="block max-w-full truncate text-left hover:underline focus-visible:underline outline-none" title={item.name}>
                        {item.name}
                    </button>
                ) : (
                    <span className="block truncate" title={item.name}>{item.name}</span>
                )}
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                    <span>{formatBytes(item.entry.size)}</span>
                    <StatusBadge status={item.entry.status} />
                </div>
            </div>
            {ready && (
                <div className="flex shrink-0 gap-1">
                    {canPreview && (
                        <Button variant="ghost" size="icon" onClick={() => onPreview(item)} aria-label={`Preview ${item.name}`} title="Preview">
                            <Eye />
                        </Button>
                    )}
                    <Button variant="ghost" size="icon" onClick={() => downloadUrl(item.entry.url, item.name)} aria-label={`Download ${item.name}`} title="Download">
                        <Download />
                    </Button>
                </div>
            )}
        </div>
    );
};

const GridItem: React.FC<RowProps> = ({ item, folderHref, selected, onToggle, onPreview }) => {
    const card = "group relative flex h-full flex-col overflow-hidden rounded-xl border bg-card text-left transition-colors hover:border-ring/60 focus-within:border-ring";

    if (item.type === 'folder')
        return (
            <Link to={folderHref(item.path)} className={cn(card, "outline-none")}>
                <div className="flex aspect-[4/3] items-center justify-center bg-muted/50"><FileIcon kind="folder" className="size-12" /></div>
                <div className="p-2.5">
                    <p className="truncate text-sm font-medium">{item.name}</p>
                    <FolderMeta item={item} />
                </div>
            </Link>
        );

    const ready = item.entry.status === 'uploaded';
    const canPreview = ready && isPreviewable(item.kind);
    const thumb = ready && item.kind === 'image'
        ? <img src={item.entry.url} alt="" loading="lazy" decoding="async" className="size-full object-cover" />
        : <FileIcon kind={item.kind} className="size-12" />;

    return (
        <div className={card}>
            <button type="button" disabled={!canPreview} onClick={() => onPreview(item)}
                    className="flex aspect-[4/3] items-center justify-center overflow-hidden bg-muted/50 outline-none disabled:cursor-default"
                    aria-label={canPreview ? `Preview ${item.name}` : item.name}>
                {thumb}
            </button>
            <div className="flex items-start gap-2 p-2.5">
                <div className="min-w-0 flex-1">
                    <p className="truncate text-sm" title={item.name}>{item.name}</p>
                    <div className="flex items-center gap-2 text-xs text-muted-foreground">
                        <span>{formatBytes(item.entry.size)}</span>
                        <StatusBadge status={item.entry.status} />
                    </div>
                </div>
                {ready && (
                    <Button variant="ghost" size="icon" className="size-7" onClick={() => downloadUrl(item.entry.url, item.name)}
                            aria-label={`Download ${item.name}`} title="Download">
                        <Download />
                    </Button>
                )}
            </div>
            <Checkbox checked={selected.has(item.path)} onCheckedChange={() => onToggle(item.path)} disabled={!ready}
                      aria-label={`Select ${item.name}`}
                      className={cn("absolute left-2 top-2 bg-background shadow-sm",
                                    !selected.has(item.path) && "opacity-0 group-hover:opacity-100 focus-visible:opacity-100")} />
        </div>
    );
};

const FolderMeta: React.FC<{ item: Extract<Item, { type: 'folder' }> }> = ({ item }) => (
    <span className="flex shrink-0 items-center gap-2 text-xs text-muted-foreground">
        <span>{item.count} {item.count === 1 ? 'item' : 'items'} · {formatBytes(item.size)}</span>
        {item.uploading > 0 && <StatusBadge status="uploading" label={`${item.uploading} uploading`} />}
        {item.failed > 0 && <StatusBadge status="failed" label={`${item.failed} failed`} />}
    </span>
);

export default ItemList;
