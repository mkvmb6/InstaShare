import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { Loader, Moon, Sun } from 'lucide-react';
import { zipSync } from 'fflate';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbSeparator } from './components/ui/breadcrumb';
import React from 'react';
import { useTheme } from 'next-themes';

const formatBytes = (bytes: number) => {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

const FolderViewer = () => {
  const { folderId = '', subPath = '' } = useParams();
  const [files, setFiles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [downloading, setDownloading] = useState(false);
  const [downloadingIndividually, setDownloadingIndividually] = useState(false);
  const [selectedFiles, setSelectedFiles] = useState(new Set());
  const navigate = useNavigate();
  const { theme, setTheme } = useTheme();

  const baseUrl = 'https://cdn.instashare.mohitkumarverma.com';

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

  const getItemCountInFolder = (folderName: string) => {
    const folderPrefix = (currentPath ? currentPath + '/' : '') + folderName + '/';
    return files.filter((f: any) => f.path.startsWith(folderPrefix)).length;
  }

  const getFolderSize = (folderName?: string) => {
    const folderPrefix = (currentPath ? currentPath + '/' : '') + (folderName ? folderName + '/' : '');
    return files
      .filter((f: any) => f.path.startsWith(folderPrefix))
      .reduce((sum, f: any) => sum + (f.size || 0), 0);
  };

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

        await new Promise(resolve => setTimeout(resolve, 500));
      } catch (e) {
        console.error('Failed to download file:', file.path);
      }
    }
    setDownloadingIndividually(false);
  };

  const downloadSelectedFiles = async () => {
    setDownloadingIndividually(true);
    for (const file of filteredFiles.filter((f: any) => selectedFiles.has(f.path))) {
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

        await new Promise(resolve => setTimeout(resolve, 500));
      } catch (e) {
        console.error('Failed to download file:', file.path);
      }
    }
    setDownloadingIndividually(false);
  };

  const toggleFileSelection = (filePath: string) => {
    setSelectedFiles(prev => {
      const newSet = new Set(prev);
      if (newSet.has(filePath)) {
        newSet.delete(filePath);
      } else {
        newSet.add(filePath);
      }
      return newSet;
    });
  };

  const toggleAllSelection = () => {
    const allPaths = filteredFiles.map((f: any) => f.path);
    const allSelected = allPaths.every((p: any) => selectedFiles.has(p));
    if (allSelected) {
      setSelectedFiles(new Set());
    } else {
      setSelectedFiles(new Set(allPaths));
    }
  };

  const renderBreadcrumbs = () => {
    const segments = currentPath.split('/').filter(Boolean);
    return (
      <Breadcrumb className="mb-4 flex flex-wrap items-center gap-1">
        <BreadcrumbItem>
          <BreadcrumbLink asChild>
            <Link to={`/view/${folderId}`}>{folderId}</Link>
          </BreadcrumbLink>
        </BreadcrumbItem>
        {segments.map((segment, index) => {
          const path = segments.slice(0, index + 1).join('/');
          return (
            <React.Fragment key={index}>
              <BreadcrumbSeparator className="flex" />
              <BreadcrumbItem>
                <BreadcrumbLink asChild>
                  <Link to={`/view/${folderId}/${encodeURIComponent(path)}`}>{segment}</Link>
                </BreadcrumbLink>
              </BreadcrumbItem>
            </React.Fragment>
          );
        })}
      </Breadcrumb>
    );
  };


  const toggleTheme = () => {
    setTheme(theme === 'dark' ? 'light' : 'dark');
  };

  if (loading) return (
    <>
      <div className="p-4 max-w-4xl mx-auto">
        <h1 className="text-4xl font-semibold mb-4">InstaShare</h1>
        <p>Loading folder contents...</p>
      </div>
    </>
  );

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <h1 className="text-4xl font-semibold mb-4">InstaShare</h1>
      <div className="flex justify-between items-center mb-2 gap-2 flex-wrap">
        <h2 className="text-xl font-semibold">
          📁 {currentPath || folderId}<span className="text-base font-normal text-muted-foreground ml-2">({formatBytes(getFolderSize())})</span>
        </h2>
        <div className="flex gap-2 flex-wrap">
          <Button onClick={downloadSelectedFiles} disabled={downloadingIndividually || selectedFiles.size === 0}>
            {downloadingIndividually ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Downloading...</span>
            ) : (
              'Download Selected'
            )}
          </Button>
          <Button onClick={downloadAllIndividually} disabled={downloadingIndividually}>
            {downloadingIndividually ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Downloading...</span>
            ) : (
              'Download Individually'
            )}
          </Button>
          <Button onClick={downloadAllAsZip} disabled={downloading || (filteredFiles.length === 1 && subFolders.length === 0)}>
            {downloading ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Zipping...</span>
            ) : (
              'Download All as ZIP'
            )}
          </Button>
          <div className="flex items-center gap-2 flex-wrap">
            <Button variant="ghost" size="icon" onClick={toggleTheme} title="Toggle Theme">
              {theme === 'dark' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
            </Button>
          </div>
        </div>
      </div>

      {renderBreadcrumbs()}

      <div className="grid gap-4">
        {subFolders.map((folder, idx) => (
          <Card key={`folder-${idx}`} className="p-3 bg-muted cursor-pointer hover:bg-accent" onClick={() => navigate(`/view/${folderId}/${encodeURIComponent((currentPath + '/' + folder).replace(/^\//, ''))}`)}>
            <CardContent className="font-medium flex justify-between items-center">
              <span>📁 {folder}</span>
              <span className="text-sm text-muted-foreground">
                {getItemCountInFolder(folder)} item(s) • {formatBytes(getFolderSize(folder))}
              </span>
            </CardContent>
          </Card>
        ))}

        {filteredFiles.length > 0 && (
          <label className="flex items-center gap-2 mb-2">
            <Checkbox
              checked={filteredFiles.every((f: any) => selectedFiles.has(f.path))}
              onCheckedChange={toggleAllSelection}>
            </Checkbox>
            <span className="text-sm">Select All</span>
          </label>
        )}

        {filteredFiles.map((file: any, index: number) => (
          <Card key={index} className="p-3">
            <CardContent className="flex items-center justify-between gap-4">
              <label className="flex items-center gap-2">
                <Checkbox
                  checked={selectedFiles.has(file.path)}
                  onCheckedChange={() => toggleFileSelection(file.path)}
                />
                <span className="truncate max-w-xs" title={file.path}>📄 {file.path.split('/').pop()}</span>
                {file.size && <span className="text-sm text-muted-foreground ml-2">({formatBytes(file.size)})</span>}
              </label>
              <Button variant="outline" onClick={() => window.open(file.url, '_blank')}>Download</Button>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
};

export default FolderViewer;
