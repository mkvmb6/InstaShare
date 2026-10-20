import { FileArchive, FileAudio, FileCode, FileImage, FileText, FileVideo, File as FileGeneric, Folder } from "lucide-react";
import type { FileKind } from "@/models/fileEntry";
import { cn } from "@/lib/utils";

const ICONS: Record<FileKind | 'folder', { icon: typeof FileGeneric; color: string }> = {
    folder: { icon: Folder, color: "text-sky-500" },
    image: { icon: FileImage, color: "text-emerald-500" },
    video: { icon: FileVideo, color: "text-violet-500" },
    audio: { icon: FileAudio, color: "text-pink-500" },
    pdf: { icon: FileText, color: "text-red-500" },
    markdown: { icon: FileText, color: "text-slate-500" },
    text: { icon: FileCode, color: "text-slate-500" },
    archive: { icon: FileArchive, color: "text-amber-500" },
    other: { icon: FileGeneric, color: "text-muted-foreground" },
};

/** Decorative: the file name next to it already says what it is. */
const FileIcon: React.FC<{ kind: FileKind | 'folder'; className?: string }> = ({ kind, className }) => {
    const { icon: Icon, color } = ICONS[kind];
    return <Icon aria-hidden className={cn("size-5 shrink-0", color, className)} />;
};

export default FileIcon;
