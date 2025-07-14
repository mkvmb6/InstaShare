import { formatBytes } from "@/utils/utils";
import { Button } from "../ui/button";
import { Card, CardContent } from "../ui/card";
import { Checkbox } from "../ui/checkbox";
import type { FileEntry } from "@/models/fileEntry";

const FileCard: React.FC<{
    file: FileEntry;
    checked: boolean;
    onCheck: () => void;
}> = ({ file, checked, onCheck }) => (
    <Card className="p-3">
        <CardContent className="flex items-center justify-between gap-4">
            <label className="flex items-center gap-2">
                <Checkbox checked={checked} onCheckedChange={onCheck} disabled={file.status === 'uploading'} />
                <span className="truncate max-w-xs" title={file.path}>📄 {file.path.split('/').pop()}</span>
                {file.size && <span className="text-sm text-muted-foreground ml-2">({formatBytes(file.size)})</span>}
            </label>
            {file.status !== 'uploading' ? (
                <Button variant="outline" onClick={() => window.open(file.url, '_blank')}>Download</Button>
            ) : (
                <span className="text-sm text-orange-500">Uploading...</span>
            )}
        </CardContent>
    </Card>
);

export default FileCard;