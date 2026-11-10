import { useEffect, useId, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import FileIcon from '@/components/custom/FileIcon';
import { Shell } from '@/FolderViewer';
import { useNow } from '@/hooks/useShare';
import { cn } from '@/lib/utils';
import { clearRecent, forgetShare, loadRecent, type RecentShare } from '@/utils/recent';
import { formatDuration, shareLinkTarget } from '@/utils/utils';

const HINT = 'Paste the whole link, or just the part after /view/.';

/** What people see at the site's root: a way into a share, and what InstaShare is. */
const HomePage: React.FC = () => {
    const navigate = useNavigate();
    const [value, setValue] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [recent, setRecent] = useState<RecentShare[]>(() => loadRecent());
    const now = useNow();
    const inputId = useId();
    const hintId = useId();
    const prefix = `${window.location.host}/view/`;

    useEffect(() => {
        document.title = 'InstaShare';
    }, []);

    const change = (text: string) => {
        setError(null);
        // A whole link pasted in is cut down to the part after the fixed "/view/" shown beside the field.
        const target = /^\s*(https?:\/\/|[\w.-]+\.[a-z]{2,}\/)/i.test(text) ? shareLinkTarget(text, window.location.hostname) : null;
        setValue(target ? displayPath(target) : text);
    };

    const open = (e: React.FormEvent) => {
        e.preventDefault();
        const target = shareLinkTarget(value, window.location.hostname);
        if (target) navigate(target);
        else setError(value.trim() ? "That isn't an InstaShare link. Paste the link exactly as you were sent it." : 'Paste the link you were sent first.');
    };

    return (
        <Shell>
            <div className="max-w-2xl pt-6 sm:pt-16">
                <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">Open a shared link</h1>
                <p className="mt-2 text-muted-foreground">If someone sent you files with InstaShare, paste the link to preview or download them.</p>

                <form onSubmit={open} className="mt-6" noValidate>
                    <label htmlFor={inputId} className="sr-only">Shared link</label>
                    <div className={cn(
                        'flex items-center gap-2 rounded-xl border bg-card py-1.5 pr-1.5 pl-4 shadow-xs transition-[box-shadow,border-color]',
                        'focus-within:border-ring focus-within:ring-[3px] focus-within:ring-ring/40',
                        error && 'border-destructive focus-within:border-destructive focus-within:ring-destructive/25',
                    )}>
                        {/* The shape of every link, so it's clear what goes in the field. Hidden where it wouldn't fit. */}
                        <span aria-hidden className="hidden shrink-0 select-none text-lg text-muted-foreground sm:inline">{prefix}</span>
                        <input
                            id={inputId}
                            value={value}
                            onChange={(e) => change(e.target.value)}
                            placeholder="Paste a link"
                            autoComplete="off"
                            autoCapitalize="off"
                            spellCheck={false}
                            inputMode="url"
                            aria-invalid={!!error}
                            aria-describedby={hintId}
                            className="min-w-0 flex-1 bg-transparent py-1.5 text-lg outline-none placeholder:text-muted-foreground/70 sm:-ml-2 sm:placeholder:text-transparent"
                        />
                        <Button type="submit" size="lg">Open</Button>
                    </div>
                    <p id={hintId} aria-live="polite" className={cn('mt-2 text-sm', error ? 'text-destructive' : 'text-muted-foreground')}>
                        {error ?? HINT}
                    </p>
                </form>

                {recent.length > 0 && (
                    <section className="mt-12" aria-labelledby="recent-heading">
                        <div className="flex items-center justify-between gap-4">
                            <div>
                                <h2 id="recent-heading" className="font-semibold">Recently opened</h2>
                                <p className="text-sm text-muted-foreground">Kept only in this browser.</p>
                            </div>
                            <Button variant="ghost" size="sm" onClick={() => { clearRecent(); setRecent([]); }}>Clear list</Button>
                        </div>
                        <ul className="mt-3 divide-y rounded-xl border bg-card">
                            {recent.map((r) => (
                                <li key={r.id} className="flex items-center gap-3 py-1.5 pr-1.5 pl-3 sm:pl-4">
                                    <FileIcon kind={r.kind} />
                                    <Link to={`/view/${encodeURIComponent(r.id)}`} className="min-w-0 flex-1 py-1 outline-none hover:underline focus-visible:underline">
                                        <span className="block truncate">{r.name}</span>
                                        <span className="block text-xs text-muted-foreground">{describe(r, now)}</span>
                                    </Link>
                                    <Button variant="ghost" size="icon" aria-label={`Remove ${r.name} from this list`} title="Remove from list"
                                            onClick={() => { forgetShare(r.id); setRecent(loadRecent()); }}>
                                        <X />
                                    </Button>
                                </li>
                            ))}
                        </ul>
                    </section>
                )}

                <section className="mt-12 max-w-xl" aria-labelledby="how-heading">
                    <h2 id="how-heading" className="font-semibold">How InstaShare works</h2>
                    <ol className="mt-3 list-decimal space-y-2 pl-5 text-muted-foreground marker:text-foreground/50">
                        <li>The sender right-clicks files or folders in Windows and chooses <span className="text-foreground">Share with InstaShare</span>.</li>
                        <li>They send you the link. It works straight away, even while the files are still uploading.</li>
                        <li>You preview or download in your browser, with no account. The link stops working when it expires, after anything from 1 hour to 7 days.</li>
                    </ol>
                    <p className="mt-6 text-sm text-muted-foreground">
                        Sending files needs the InstaShare app for Windows and a key from whoever runs this InstaShare.
                    </p>
                </section>
            </div>
        </Shell>
    );
};

function describe(r: RecentShare, now: number) {
    const left = r.expiresUtc ? Date.parse(r.expiresUtc) - now : NaN;
    if (Number.isFinite(left)) return `Expires in ${formatDuration(left)}`;
    return `Opened ${formatDuration(now - Date.parse(r.openedAt))} ago`;
}

/** "/view/My%20Trip/Day%201" → "My Trip/Day 1", for showing in the field. */
function displayPath(target: string) {
    return target.slice('/view/'.length).split('/').map((s) => {
        try {
            return decodeURIComponent(s);
        } catch {
            return s;
        }
    }).join('/');
}

export default HomePage;
