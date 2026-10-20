import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Clock, Download, Ellipsis, FolderDown, LoaderCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import Breadcrumbs from '@/components/custom/Breadcrumbs';
import ItemList from '@/components/custom/ItemList';
import PreviewDialog from '@/components/custom/PreviewDialog';
import ShareMenu from '@/components/custom/ShareMenu';
import SingleFileView from '@/components/custom/SingleFileView';
import ThemeMenu from '@/components/custom/ThemeMenu';
import Toolbar, { type SortKey } from '@/components/custom/Toolbar';
import { LoadError, Loading, MissingShare } from '@/components/custom/States';
import { usePreview } from '@/hooks/usePreview';
import { useNow, useShare } from '@/hooks/useShare';
import type { FileEntry, Item } from '@/models/fileEntry';
import { encodePath, formatBytes, formatDuration, isPreviewable, itemsAt, normalizePath, smartCompare, summarize } from '@/utils/utils';
import { downloadUrl } from '@/utils/download';

type FileItem = Extract<Item, { type: 'file' }>;

const VIEW_KEY = 'instashare:view';

const readView = (): 'list' | 'grid' => {
    try {
        return localStorage.getItem(VIEW_KEY) === 'grid' ? 'grid' : 'list';
    } catch {
        return 'list';
    }
};

/**
 * Routes: /view/:shareId and /view/:shareId/<path inside the share>.
 * For folder shares the path always starts with the shared folder's name.
 */
const FolderViewer: React.FC = () => {
    const { shareId = '', '*': splat = '' } = useParams();
    // The router has already decoded the params; decoding again breaks names containing '%'.
    const routePath = splat.replace(/\/+$/, '');
    const { state, retry } = useShare(shareId);

    if (state.status === 'loading') return <Shell><Loading /></Shell>;
    if (state.status === 'missing') return <Shell><MissingShare /></Shell>;
    if (state.status === 'error') return <Shell><LoadError onRetry={retry} /></Shell>;
    return <ShareView shareId={shareId} routePath={routePath} files={state.index.files} name={state.index.name} expiresUtc={state.index.expiresUtc} />;
};

const ShareView: React.FC<{ shareId: string; routePath: string; files: FileEntry[]; name?: string; expiresUtc?: string }> = ({ shareId, routePath, files, name, expiresUtc }) => {
    // A folder share's paths all start with the folder's name; that's the share's real root.
    const root = useMemo(() => {
        const first = files[0] ? normalizePath(files[0].path) : '';
        const top = first.split('/')[0];
        const isFolder = files.length > 0 && files.every((f) => normalizePath(f.path).startsWith(top + '/'));
        return isFolder ? top : '';
    }, [files]);

    const currentPath = routePath || root;
    const isSingleFile = !root && files.length === 1;
    const title = name ?? (root || (files[0] ? normalizePath(files[0].path) : 'Shared files'));

    const totals = useMemo(() => summarize(files), [files]);
    const here = useMemo(() => summarize(files, currentPath), [files, currentPath]);

    useEffect(() => {
        document.title = `${title} · InstaShare`;
    }, [title]);

    const segments = currentPath ? currentPath.split('/') : [];
    const base = `/view/${encodeURIComponent(shareId)}`;
    const folderHref = useCallback((path: string) => `${base}/${encodePath(path)}`, [base]);
    const hrefFor = (depth: number) => (depth === 0 && !routePath ? base : folderHref(segments.slice(0, depth + 1).join('/')));

    return (
        <Shell
            title={isSingleFile ? undefined : segments.at(-1) ?? title}
            subtitle={<ShareMeta count={here.count} size={here.size} expiresUtc={expiresUtc} isSingleFile={isSingleFile} />}
            actions={<ShareMenu title={title} />}
        >
            <UploadProgress uploaded={totals.uploaded} total={totals.count} uploading={totals.uploading} />
            {isSingleFile ? (
                <SingleFile item={itemsAt(files, '')[0] as FileItem} />
            ) : (
                <>
                    <Breadcrumbs segments={segments} hrefFor={hrefFor} />
                    <FolderContents key={currentPath} files={files} currentPath={currentPath} folderHref={folderHref} zipName={segments.at(-1) ?? title} />
                </>
            )}
        </Shell>
    );
};

const SingleFile: React.FC<{ item: FileItem }> = ({ item }) => {
    const previewable = useMemo(() => (item.entry.status === 'uploaded' && isPreviewable(item.kind) ? [item] : []), [item]);
    const preview = usePreview(previewable);
    return (
        <>
            <SingleFileView item={item} onExpand={() => preview.open(item.path)} />
            <PreviewDialog items={previewable} index={preview.index} onIndexChange={preview.setIndex} />
        </>
    );
};

/** Keyed by folder, so search, selection and the open preview reset when you change folder. */
const FolderContents: React.FC<{
    files: FileEntry[];
    currentPath: string;
    folderHref: (path: string) => string;
    zipName: string;
}> = ({ files, currentPath, folderHref, zipName }) => {
    const [query, setQuery] = useState('');
    const [sort, setSort] = useState<SortKey>('name');
    const [view, setView] = useState<'list' | 'grid'>(readView);
    const [selected, setSelected] = useState<Set<string>>(new Set());
    const [busy, setBusy] = useState<'zip' | 'files' | null>(null);

    const changeView = (v: 'list' | 'grid') => {
        setView(v);
        try {
            localStorage.setItem(VIEW_KEY, v);
        } catch {
            // Private mode; the choice just won't stick.
        }
    };

    const items = useMemo(() => {
        const q = query.trim().toLowerCase();
        const list = itemsAt(files, currentPath).filter((i) => !q || i.name.toLowerCase().includes(q));
        const size = (i: Item) => (i.type === 'folder' ? i.size : i.entry.size ?? 0);
        const ext = (i: Item) => (i.type === 'folder' ? '' : i.name.split('.').pop() ?? '');
        return list.sort((a, b) => {
            // Folders first, always.
            if (a.type !== b.type) return a.type === 'folder' ? -1 : 1;
            if (sort === 'size') return size(b) - size(a) || smartCompare(a.name, b.name);
            if (sort === 'type') return smartCompare(ext(a), ext(b)) || smartCompare(a.name, b.name);
            return smartCompare(a.name, b.name);
        });
    }, [files, currentPath, query, sort]);

    const fileItems = useMemo(() => items.filter((i): i is FileItem => i.type === 'file'), [items]);
    const ready = useMemo(() => fileItems.filter((i) => i.entry.status === 'uploaded'), [fileItems]);
    const previewable = useMemo(() => ready.filter((i) => isPreviewable(i.kind)), [ready]);
    const preview = usePreview(previewable);

    // Only count selections that are still visible and downloadable.
    const selectedReady = ready.filter((i) => selected.has(i.path));

    const toggle = (path: string) =>
        setSelected((prev) => {
            const next = new Set(prev);
            if (!next.delete(path)) next.add(path);
            return next;
        });
    const toggleAll = () => setSelected(selectedReady.length === ready.length ? new Set() : new Set(ready.map((i) => i.path)));

    const downloadEach = async (list: FileItem[]) => {
        setBusy('files');
        for (const item of list) {
            downloadUrl(item.entry.url, item.name);
            // Browsers block a burst of downloads; a short gap lets each one through.
            await new Promise((r) => setTimeout(r, 400));
        }
        setBusy(null);
    };

    const downloadZip = async () => {
        setBusy('zip');
        const prefix = currentPath ? currentPath + '/' : '';
        const entries: Record<string, Uint8Array> = {};
        try {
            // Loaded on first use; most visitors never build a ZIP.
            const { zipSync } = await import('fflate');
            await Promise.all(
                files
                    .filter((f) => normalizePath(f.path).startsWith(prefix) && f.status === 'uploaded')
                    .map(async (f) => {
                        const res = await fetch(f.url);
                        if (!res.ok) throw new Error(`${f.path}: ${res.status}`);
                        entries[normalizePath(f.path).slice(prefix.length)] = new Uint8Array(await res.arrayBuffer());
                    })
            );
            const blob = new Blob([zipSync(entries, { level: 6 })], { type: 'application/zip' });
            const url = URL.createObjectURL(blob);
            downloadUrl(url, `${zipName}.zip`);
            setTimeout(() => URL.revokeObjectURL(url), 60_000);
        } catch (e) {
            console.error(e);
            alert("Couldn't create the ZIP. Try downloading the files one by one.");
        } finally {
            setBusy(null);
        }
    };

    const hasSubfolders = items.some((i) => i.type === 'folder');
    const downloadTarget = selectedReady.length > 0 ? selectedReady : ready;

    return (
        <>
            <Toolbar query={query} onQuery={setQuery} sort={sort} onSort={setSort} view={view} onView={changeView}
                     selectable={ready.length} selected={selectedReady.length} onToggleAll={toggleAll} />

            {items.length === 0 ? (
                <p className="rounded-xl border border-dashed p-10 text-center text-muted-foreground">
                    {query ? `Nothing here matches "${query}".` : 'This folder is empty.'}
                </p>
            ) : (
                <ItemList items={items} view={view} folderHref={folderHref} selected={selected} onToggle={toggle}
                          onPreview={(item) => preview.open(item.path)} />
            )}

            {/* Sticky on phones, so the main action is always in reach. */}
            {(ready.length > 0 || hasSubfolders) && (
                <div className="sticky bottom-0 -mx-4 mt-4 flex gap-2 border-t bg-background/90 px-4 py-3 backdrop-blur sm:static sm:mx-0 sm:border-0 sm:bg-transparent sm:px-0 sm:backdrop-blur-none">
                    <Button className="flex-1 sm:flex-none" disabled={busy !== null || downloadTarget.length === 0} onClick={() => downloadEach(downloadTarget)}>
                        {busy === 'files' ? <LoaderCircle className="animate-spin" /> : <Download />}
                        {selectedReady.length > 0 ? `Download ${selectedReady.length} selected` : 'Download all'}
                    </Button>
                    <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                            <Button variant="outline" size="icon" aria-label="More download options" disabled={busy !== null}>
                                {busy === 'zip' ? <LoaderCircle className="animate-spin" /> : <Ellipsis />}
                            </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                            <DropdownMenuItem onSelect={downloadZip}><FolderDown /> Download folder as ZIP</DropdownMenuItem>
                        </DropdownMenuContent>
                    </DropdownMenu>
                </div>
            )}

            <PreviewDialog items={previewable} index={preview.index} onIndexChange={preview.setIndex} />
        </>
    );
};

const ShareMeta: React.FC<{ count: number; size: number; expiresUtc?: string; isSingleFile: boolean }> = ({ count, size, expiresUtc, isSingleFile }) => {
    const now = useNow();
    const left = expiresUtc ? Date.parse(expiresUtc) - now : NaN;
    if (isSingleFile && !Number.isFinite(left)) return null;
    return (
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            {!isSingleFile && <span>{count} {count === 1 ? 'file' : 'files'} · {formatBytes(size)}</span>}
            {Number.isFinite(left) && (
                <span className="inline-flex items-center gap-1">
                    <Clock className="size-3.5" aria-hidden />
                    {left > 0 ? `Expires in ${formatDuration(left)}` : 'Expiring now'}
                </span>
            )}
        </span>
    );
};

/** Shown only while the sender's upload is still running. */
const UploadProgress: React.FC<{ uploaded: number; total: number; uploading: number }> = ({ uploaded, total, uploading }) => {
    if (uploading === 0 || total <= 1) return null;
    const percent = Math.round((uploaded / total) * 100);
    return (
        <div className="mb-4 rounded-xl border bg-card p-3" role="status">
            <div className="mb-2 flex items-center justify-between text-sm">
                <span className="inline-flex items-center gap-2">
                    <LoaderCircle className="size-4 animate-spin text-amber-500" aria-hidden />
                    Still uploading: {uploaded} of {total} files ready
                </span>
                <span className="text-muted-foreground">{percent}%</span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden>
                <div className="h-full rounded-full bg-amber-500 transition-[width] duration-500" style={{ width: `${percent}%` }} />
            </div>
        </div>
    );
};

export const Shell: React.FC<{ children: React.ReactNode; title?: string; subtitle?: React.ReactNode; actions?: React.ReactNode }> = ({ children, title, subtitle, actions }) => (
    <div className="mx-auto flex min-h-dvh max-w-5xl flex-col px-4 pb-6">
        <header className="flex items-center gap-2 py-4">
            <span className="mr-auto flex items-center gap-2 font-semibold">
                <img src="/favicon.svg" alt="" className="size-7" />
                <span className="text-lg">InstaShare</span>
            </span>
            {actions}
            <ThemeMenu />
        </header>
        <main className="flex-1">
            {title && (
                <div className="mb-4">
                    <h1 className="break-all text-2xl font-semibold sm:text-3xl">{title}</h1>
                    {subtitle && <div className="mt-1 text-sm text-muted-foreground">{subtitle}</div>}
                </div>
            )}
            {!title && subtitle && <div className="mb-4 text-sm text-muted-foreground">{subtitle}</div>}
            {children}
        </main>
    </div>
);

export default FolderViewer;
