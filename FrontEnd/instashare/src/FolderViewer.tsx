import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Loader } from 'lucide-react';
import { zipSync } from 'fflate';

const FolderViewer = ({ folderId }: any) => {
  const [files, setFiles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [downloading, setDownloading] = useState(false);

  const baseUrl = 'https://instashare.mohitkumarverma.com';

  useEffect(() => {
    const fetchIndex = async () => {
      try {
        const res = await fetch(`${baseUrl}/${folderId}/index.json`);
        const data = await res.json();
        setFiles(data);
      } catch (err) {
        console.error('Failed to load index.json', err);
      } finally {
        setLoading(false);
      }
    };
    fetchIndex();
  }, [folderId]);

  const downloadAllAsZip = async () => {
    setDownloading(true);
    const fileBlobs: any = {};

    await Promise.all(
      files.map(async (file: any) => {
        try {
          const response = await fetch(file.url);
          const blob = await response.blob();
          const arrayBuffer = await blob.arrayBuffer();
          fileBlobs[file.path] = new Uint8Array(arrayBuffer);
        } catch (e) {
          console.error('Failed to fetch file:', file.path);
        }
      })
    );

    const zipped = zipSync(fileBlobs, { level: 6 });
    const blob = new Blob([zipped], { type: 'application/zip' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${folderId}.zip`;
    a.click();
    setDownloading(false);
  };

  if (loading) return <p className="p-4">Loading folder contents...</p>;

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-xl font-semibold">Files in "{folderId}"</h2>
        <Button onClick={downloadAllAsZip} disabled={downloading}>
          {downloading ? (
            <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Zipping...</span>
          ) : (
            'Download All as ZIP'
          )}
        </Button>
      </div>

      <div className="grid gap-4">
        {files.map((file: any, index) => (
          <Card key={index} className="p-3">
            <CardContent className="flex items-center justify-between">
              <span className="truncate max-w-xs" title={file.path}>{file.path}</span>
              <Button variant="outline" onClick={() => window.open(file.url, '_blank')}>Download</Button>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
};

export default FolderViewer;
