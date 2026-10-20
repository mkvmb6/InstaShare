import { CircleAlert, Link2Off, LoaderCircle, RotateCw } from "lucide-react";
import { Button } from "../ui/button";

/** Shown when index.json is missing: expired, deleted, or a mistyped link. */
export const MissingShare: React.FC = () => (
    <Centered>
        <Link2Off className="size-12 text-muted-foreground" aria-hidden />
        <h1 className="text-xl font-semibold">This link has expired</h1>
        <p className="max-w-sm text-muted-foreground">
            Shared files are deleted automatically after a while. Ask the person who sent it to share it again.
        </p>
    </Centered>
);

export const LoadError: React.FC<{ onRetry: () => void }> = ({ onRetry }) => (
    <Centered>
        <CircleAlert className="size-12 text-muted-foreground" aria-hidden />
        <h1 className="text-xl font-semibold">Couldn't load this share</h1>
        <p className="max-w-sm text-muted-foreground">Check your connection and try again.</p>
        <Button onClick={onRetry}><RotateCw /> Try again</Button>
    </Centered>
);

export const Loading: React.FC = () => (
    <Centered>
        <LoaderCircle className="size-8 animate-spin text-muted-foreground" aria-hidden />
        <p className="text-muted-foreground" role="status">Loading…</p>
    </Centered>
);

export const NotFound: React.FC = () => (
    <Centered>
        <Link2Off className="size-12 text-muted-foreground" aria-hidden />
        <h1 className="text-xl font-semibold">Page not found</h1>
        <p className="max-w-sm text-muted-foreground">InstaShare links look like instashare.mohitkumarverma.com/view/…</p>
    </Centered>
);

const Centered: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <div className="flex min-h-[60dvh] flex-col items-center justify-center gap-3 px-4 text-center">{children}</div>
);
