import { formatBytes } from "@/utils/utils";
import { Card, CardContent } from "../ui/card";

interface FolderCardProps {
    folder: string | null;
    onClick: () => void;
    itemCount: number;
    size: number;
    uploading?: boolean;
}

const FolderCard: React.FC<FolderCardProps> = ({ folder, onClick, itemCount, size, uploading }) => (
    <Card className="p-3 bg-muted cursor-pointer hover:bg-accent" onClick={onClick}>
        <CardContent className="font-medium flex justify-between items-center">
            <span>📁 {folder}</span>
            <span className="text-sm text-muted-foreground">
                {itemCount} item(s) • {formatBytes(size)}
                {uploading ? <span className="ml-2 text-orange-500">Uploading...</span> : null}
            </span>
        </CardContent>
    </Card>
);

export default FolderCard;