import { useEffect, useState } from "react";
import QRCode from "qrcode";
import { Check, Copy, QrCode, Share2 } from "lucide-react";
import { Button } from "../ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "../ui/dropdown-menu";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "../ui/dialog";

/**
 * Copy link, QR code, and the device's own share sheet where the browser has one.
 */
const ShareMenu: React.FC<{ title: string }> = ({ title }) => {
    const [copied, setCopied] = useState(false);
    const [qrOpen, setQrOpen] = useState(false);
    const url = window.location.href;
    const canNativeShare = typeof navigator.share === 'function';

    const copy = async () => {
        try {
            await navigator.clipboard.writeText(url);
            setCopied(true);
            window.setTimeout(() => setCopied(false), 2000);
        } catch {
            window.prompt('Copy this link:', url);
        }
    };

    const nativeShare = () => navigator.share({ title, url }).catch(() => { /* Dismissed */ });

    return (
        <>
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <Button variant="outline" size="sm" aria-label="Share this link">
                        {copied ? <Check /> : <Share2 />} <span className="hidden sm:inline">{copied ? 'Copied' : 'Share'}</span>
                    </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                    <DropdownMenuItem onSelect={copy}><Copy /> Copy link</DropdownMenuItem>
                    <DropdownMenuItem onSelect={() => setQrOpen(true)}><QrCode /> Show QR code</DropdownMenuItem>
                    {canNativeShare && <DropdownMenuItem onSelect={nativeShare}><Share2 /> Share via…</DropdownMenuItem>}
                </DropdownMenuContent>
            </DropdownMenu>
            {/* Live region, so screen readers hear the copy confirmation. */}
            <span className="sr-only" aria-live="polite">{copied ? 'Link copied' : ''}</span>

            <Dialog open={qrOpen} onOpenChange={setQrOpen}>
                <DialogContent className="w-[min(92vw,22rem)] rounded-xl p-6 text-center">
                    <DialogTitle className="text-base font-semibold">Scan to open</DialogTitle>
                    <DialogDescription className="mt-1 text-sm text-muted-foreground">Point a phone camera at the code.</DialogDescription>
                    <QrImage url={url} />
                    <Button variant="outline" className="mt-4 w-full" onClick={copy}>
                        {copied ? <Check /> : <Copy />} {copied ? 'Copied' : 'Copy link'}
                    </Button>
                </DialogContent>
            </Dialog>
        </>
    );
};

const QrImage: React.FC<{ url: string }> = ({ url }) => {
    const [src, setSrc] = useState<string>();
    useEffect(() => {
        // Dark on white whatever the theme; inverted QR codes don't scan reliably.
        QRCode.toDataURL(url, { margin: 1, width: 480, color: { dark: '#000000', light: '#ffffff' } }).then(setSrc, console.error);
    }, [url]);
    return (
        <div className="mx-auto mt-4 aspect-square w-56 rounded-lg bg-white p-3">
            {src && <img src={src} alt="QR code for this link" className="size-full" style={{ imageRendering: 'pixelated' }} />}
        </div>
    );
};

export default ShareMenu;
