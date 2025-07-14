import { formatBytes } from "@/utils/utils";
import { Card, CardContent } from "../ui/card";

const FolderCard: React.FC<{
    folder: string | null;
    onClick: () => void;
    itemCount: number;
    size: number;
}> = ({ folder, onClick, itemCount, size }) => (
    <Card className="p-3 bg-muted cursor-pointer hover:bg-accent" onClick={onClick}>
        <CardContent className="font-medium flex justify-between items-center">
            <span>📁 {folder}</span>
            <span className="text-sm text-muted-foreground">
                {itemCount} item(s) • {formatBytes(size)}
            </span>
        </CardContent>
    </Card>
);

export default FolderCard;