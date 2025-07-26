import { formatBytes } from "@/utils/utils";
import { Button } from "../ui/button";
import { Card, CardContent } from "../ui/card";
import { Checkbox } from "../ui/checkbox";
import type { FileEntry } from "@/models/fileEntry";

// ...existing code...

import { useState } from "react";
const getFileType = (fileName: string) => {
    const ext = fileName.split('.').pop()?.toLowerCase() || '';
    const videoExts = ["mp4", "webm", "mkv", "mov", "avi", "flv", "wmv", "mpeg", "3gp"];
    const imageExts = ["jpg", "jpeg", "png", "gif", "bmp", "svg", "webp", "ico"];
    const pdfExts = ["pdf"];
    const textExts = ["txt", "md", "csv", "json", "xml", "log"];
    if (videoExts.includes(ext)) return "video";
    if (imageExts.includes(ext)) return "image";
    if (pdfExts.includes(ext)) return "pdf";
    if (textExts.includes(ext)) return "text";
    return "other";
};

const FileCard: React.FC<{
    file: FileEntry;
    checked: boolean;
    onCheck: () => void;
}> = ({ file, checked, onCheck }) => {
    const [showViewer, setShowViewer] = useState(false);
    const fileName = file.path.split('/').pop() || '';
    const fileType = getFileType(fileName);

    const handleView = () => setShowViewer(true);
    const handleCloseViewer = () => setShowViewer(false);

    return (
        <Card className="p-3">
            <CardContent className="flex items-center justify-between gap-4">
                <label className="flex items-center gap-2">
                    <Checkbox checked={checked} onCheckedChange={onCheck} disabled={file.status === 'uploading'} />
                    <span className="truncate max-w-xs" title={file.path}>📄 {fileName}</span>
                    {file.size && <span className="text-sm text-muted-foreground ml-2">({formatBytes(file.size)})</span>}
                </label>
                {file.status === 'uploaded' ? (
                    <div className="flex gap-2">
                        {fileType === "video" && (
                            <Button variant="outline" onClick={handleView}>Watch</Button>
                        )}
                        {(fileType === "image" || fileType === "pdf" || fileType === "text") && (
                            <Button variant="outline" onClick={handleView}>View</Button>
                        )}
                        <Button variant="outline" onClick={() => window.open(file.url, '_blank')}>Download</Button>
                    </div>
                ) : (
                    <span className="text-sm text-orange-500">Uploading...</span>
                )}
            </CardContent>
            {showViewer && file.status === 'uploaded' && (
                <div className="fixed inset-0 bg-black bg-opacity-70 flex items-center justify-center z-50">
                    <div className="bg-white rounded shadow-lg p-4 max-w-2xl w-full relative">
                        <Button variant="ghost" size="sm" className="absolute top-2 right-2" onClick={handleCloseViewer}>Close</Button>
                        {fileType === "video" ? (
                            <video src={file.url} controls autoPlay className="w-full h-auto max-h-[70vh]" />
                        ) : fileType === "image" ? (
                            <img src={file.url} alt={fileName} className="w-full h-auto max-h-[70vh] object-contain" />
                        ) : fileType === "pdf" ? (
                            <iframe src={file.url} title={fileName} className="w-full h-[70vh]" />
                        ) : fileType === "text" ? (
                            <iframe src={file.url} title={fileName} className="w-full h-[70vh] bg-gray-100" />
                        ) : (
                            <div className="text-center">Cannot preview this file type.</div>
                        )}
                    </div>
                </div>
            )}
        </Card>
    );
};

export default FileCard;