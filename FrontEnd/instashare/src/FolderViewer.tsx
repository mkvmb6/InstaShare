import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Loader } from 'lucide-react';
import { zipSync } from 'fflate';
import { useNavigate, useParams, Link } from 'react-router-dom';

const FolderViewer = () => {
  const { folderId = '', subPath = '' } = useParams();
  const [files, setFiles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [downloading, setDownloading] = useState(false);
  const [downloadingIndividually, setDownloadingIndividually] = useState(false);
  const navigate = useNavigate();

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

  const currentPath = decodeURIComponent(subPath || '');

  const filteredFiles: any = files.filter((file: any) => {
    const relPath = file.path.replace(/\\/g, '/');
    if (!relPath.startsWith(currentPath)) return false;
    const sub = relPath.substring(currentPath.length).replace(/^\//, '');
    return !sub.includes('/') || sub.split('/').length === 1;
  });

  const subFolders = Array.from(new Set(
    files
      .filter((f: any) => f.path.startsWith(currentPath))
      .map((f: any) => {
        const sub = f.path.substring(currentPath.length).replace(/^\//, '');
        const parts = sub.split('/');
        return parts.length > 1 ? parts[0] : null;
      })
      .filter(Boolean)
  ));

  const downloadAllAsZip = async () => {
    setDownloading(true);
    const zipEntries: any = {};

    await Promise.all(
      files
        .filter((f: any) => f.path.startsWith(currentPath))
        .map(async (file: any) => {
          const url = file.url;
          try {
            const response = await fetch(url);
            const blob = await response.blob();
            const buffer = await blob.arrayBuffer();
            const relativePath = file.path.substring(currentPath.length).replace(/^\//, '');
            zipEntries[relativePath] = new Uint8Array(buffer);
          } catch (e) {
            console.error('Failed to fetch file:', file.path);
          }
        })
    );

    const zipped = zipSync(zipEntries, { level: 6 });
    const blob = new Blob([zipped], { type: 'application/zip' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = (currentPath || folderId) + '.zip';
    a.click();
    setDownloading(false);
  };

  const downloadAllIndividually = async () => {
    setDownloadingIndividually(true);

    for (const file of filteredFiles) {
      try {
        const response = await fetch(file.url);
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = url;
        a.download = file.path.split('/').pop() || 'file';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);

        URL.revokeObjectURL(url);
        await new Promise(resolve => setTimeout(resolve, 500)); // Optional delay
      } catch (e) {
        console.error('Failed to download', file.path, e);
      }
    }

    setDownloadingIndividually(false);
  };



  const renderBreadcrumbs = () => {
    const segments = currentPath.split('/').filter(Boolean);
    const links = [<Link key="root" to={`/view/${folderId}`} className="text-blue-600 hover:underline">{folderId}</Link>];

    segments.reduce((acc: string[], segment, index) => {
      const path = [...acc, segment].join('/');
      links.push(
        <span key={`sep-${index}`}> / </span>,
        <Link key={`crumb-${index}`} to={`/view/${folderId}/${encodeURIComponent(path)}`} className="text-blue-600 hover:underline">
          {segment}
        </Link>
      );
      return [...acc, segment];
    }, []);

    return <div className="mb-4">{links}</div>;
  };

  if (loading) return <p className="p-4">Loading folder contents...</p>;

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex justify-between items-center mb-2 gap-2 flex-wrap">
        <h2 className="text-xl font-semibold">📁 {currentPath || folderId}</h2>
        <div className="flex gap-2">
          <Button onClick={downloadAllAsZip} disabled={downloading}>
            {downloading ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Zipping...</span>
            ) : (
              'Download All as ZIP'
            )}
          </Button>
          <Button onClick={downloadAllIndividually} disabled={downloadingIndividually}>
            {downloadingIndividually ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Downloading...</span>
            ) : (
              'Download Individually'
            )}
          </Button>
        </div>
      </div>

      {renderBreadcrumbs()}

      <div className="grid gap-4">
        {subFolders.map((folder, idx) => (
          <Card key={`folder-${idx}`} className="p-3 bg-muted cursor-pointer hover:bg-accent" onClick={() => navigate(`/view/${folderId}/${encodeURIComponent((currentPath + '/' + folder).replace(/^\//, ''))}`)}>
            <CardContent className="font-medium">📁 {folder}</CardContent>
          </Card>
        ))}

        {filteredFiles.map((file: any, index: number) => (
          <Card key={index} className="p-3">
            <CardContent className="flex items-center justify-between">
              <span className="truncate max-w-xs" title={file.path}>📄 {file.path.split('/').pop()}</span>
              <Button variant="outline" onClick={() => window.open(file.url, '_blank')}>Download</Button>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
};

export default FolderViewer;
