import { CircleAlert, LoaderCircle } from "lucide-react";
import type { FileStatus } from "@/models/fileEntry";

/** Status in words and an icon, not colour alone. Renders nothing for uploaded files. */
const StatusBadge: React.FC<{ status: FileStatus | 'partial'; label?: string }> = ({ status, label }) => {
    if (status === 'uploaded') return null;
    if (status === 'failed')
        return (
            <span className="inline-flex items-center gap-1 text-xs font-medium text-red-600 dark:text-red-400">
                <CircleAlert className="size-3.5" aria-hidden /> {label ?? 'Upload failed'}
            </span>
        );
    return (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-amber-600 dark:text-amber-400">
            <LoaderCircle className="size-3.5 animate-spin" aria-hidden /> {label ?? 'Uploading'}
        </span>
    );
};

export default StatusBadge;
