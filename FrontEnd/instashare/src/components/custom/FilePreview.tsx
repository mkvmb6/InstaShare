import { lazy, Suspense, useEffect, useState } from "react";
import { Download, FileQuestion, LoaderCircle } from "lucide-react";
import { Button } from "../ui/button";
import type { Item } from "@/models/fileEntry";
import { formatBytes } from "@/utils/utils";
import { downloadUrl } from "@/utils/download";

type FileItem = Extract<Item, { type: 'file' }>;

const MarkdownView = lazy(() => import("./MarkdownView"));
const Spinner = () => <LoaderCircle className="size-6 animate-spin text-muted-foreground" aria-label="Loading" />;

// Bigger text files show their beginning only, so a 2 GB log doesn't freeze the tab.
const MAX_TEXT_BYTES = 512 * 1024;

/** The body of the preview dialog for one file. */
const FilePreview: React.FC<{ item: FileItem }> = ({ item }) => {
    const { url } = item.entry;
    switch (item.kind) {
        case 'image':
            return <img src={url} alt={item.name} className="max-h-full max-w-full object-contain" />;
        case 'video':
            return <video src={url} controls autoPlay playsInline className="max-h-full max-w-full bg-black" />;
        case 'audio':
            return (
                <div className="flex w-full max-w-md flex-col items-center gap-4 p-6">
                    <p className="text-center font-medium break-all">{item.name}</p>
                    <audio src={url} controls autoPlay className="w-full" />
                </div>
            );
        case 'pdf':
            return <iframe src={url} title={item.name} className="h-full w-full border-0 bg-white" />;
        case 'markdown':
        case 'text':
            return <TextPreview item={item} />;
        default:
            return <NoPreview item={item} />;
    }
};

const TextPreview: React.FC<{ item: FileItem }> = ({ item }) => {
    const [state, setState] = useState<{ text: string; truncated: boolean } | 'loading' | 'error'>('loading');

    useEffect(() => {
        const controller = new AbortController();
        const size = item.entry.size ?? 0;
        const truncated = size > MAX_TEXT_BYTES;
        fetch(item.entry.url, {
            signal: controller.signal,
            headers: truncated ? { Range: `bytes=0-${MAX_TEXT_BYTES - 1}` } : undefined,
        })
            .then((res) => (res.ok ? res.text() : Promise.reject(new Error(String(res.status)))))
            // A server that ignores Range sends everything; cut it here instead.
            .then((text) => setState({ text: truncated ? text.slice(0, MAX_TEXT_BYTES) : text, truncated }))
            .catch((e) => !controller.signal.aborted && (console.error(e), setState('error')));
        return () => controller.abort();
    }, [item.entry.url, item.entry.size]);

    if (state === 'loading')
        return <Spinner />;
    if (state === 'error')
        return <p className="text-sm text-muted-foreground">Couldn't load this file. Try downloading it instead.</p>;

    const notice = state.truncated && (
        <p className="sticky top-0 border-b bg-amber-50 px-4 py-2 text-xs text-amber-900 dark:bg-amber-950 dark:text-amber-200">
            Showing the first {formatBytes(MAX_TEXT_BYTES)} of {formatBytes(item.entry.size)}. Download the file to see all of it.
        </p>
    );

    return (
        <div className="h-full w-full overflow-auto bg-background">
            {notice}
            {item.kind === 'markdown' ? (
                <Suspense fallback={<div className="flex justify-center p-10"><Spinner /></div>}>
                    <MarkdownView text={state.text} />
                </Suspense>
            ) : (
                <pre tabIndex={0} className="min-h-full p-4 font-mono text-[13px] leading-relaxed whitespace-pre-wrap break-words outline-none">
                    {state.text}
                </pre>
            )}
        </div>
    );
};

const NoPreview: React.FC<{ item: FileItem }> = ({ item }) => (
    <div className="flex flex-col items-center gap-3 p-8 text-center">
        <FileQuestion className="size-12 text-muted-foreground" aria-hidden />
        <p className="font-medium">No preview for this file type</p>
        <p className="text-sm text-muted-foreground">{formatBytes(item.entry.size)}</p>
        <Button onClick={() => downloadUrl(item.entry.url, item.name)}>
            <Download /> Download
        </Button>
    </div>
);

export default FilePreview;
