import React, { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Loader, Moon, Sun } from 'lucide-react';
import { zipSync } from 'fflate';
import { useNavigate, useParams } from 'react-router-dom';
import { useTheme } from 'next-themes';
import type { FileEntry } from './models/fileEntry';
import { formatBytes, smartCompare } from './utils/utils';
import Breadcrumbs from './components/custom/Breadcrumbs';
import FolderCard from './components/custom/FolderCard';
import FileCard from './components/custom/FileCard';

const baseUrl = 'https://cdn.instashare.mohitkumarverma.com';


const useFolderData = (folderId: string, subPath: string) => {
  const [files, setFiles] = useState<FileEntry[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let isMounted = true;
    const fetchIndex = async () => {
      try {
        const res = await fetch(`${baseUrl}/${folderId}/index.json`);
        const data = await res.json();
        if (isMounted) setFiles(data);
      } catch (err) {
        if (isMounted) console.error('Failed to load index.json', err);
      } finally {
        if (isMounted) setLoading(false);
      }
    };
    fetchIndex();

    const interval = setInterval(fetchIndex, 3000);
    return () => {
      isMounted = false;
      clearInterval(interval);
    };
  }, [folderId, subPath]);

  return { files, loading };
};

const getFilteredFiles = (files: FileEntry[], currentPath: string) =>
  files.filter(file => {
    const relPath = file.path.replace(/\\/g, '/');
    if (!relPath.startsWith(currentPath)) return false;
    const sub = relPath.substring(currentPath.length).replace(/^\//, '');
    return !sub.includes('/') || sub.split('/').length === 1;
  });

const getSubFolders = (files: FileEntry[], currentPath: string) =>
  Array.from(new Set(
    files
      .filter(f => f.path.startsWith(currentPath))
      .map(f => {
        const sub = f.path.substring(currentPath.length).replace(/^\//, '');
        const parts = sub.split('/');
        return parts.length > 1 ? parts[0] : null;
      })
      .filter(Boolean)
  ));

const getItemCountInFolder = (files: FileEntry[], currentPath: string, folderName: string | null) => {
  const folderPrefix = (currentPath ? currentPath + '/' : '') + folderName + '/';
  return files.filter(f => f.path.startsWith(folderPrefix)).length;
};

const getFolderSize = (files: FileEntry[], currentPath: string, folderName?: string | null) => {
  const folderPrefix = (currentPath ? currentPath + '/' : '') + (folderName ? folderName + '/' : '');
  return files
    .filter(f => f.path.startsWith(folderPrefix))
    .reduce((sum, f) => sum + (f.size || 0), 0);
};


const FolderViewer: React.FC = () => {
  const { folderId = '', subPath = '' } = useParams();
  const { files, loading } = useFolderData(folderId, subPath || '');
  const [downloading, setDownloading] = useState(false);
  const [downloadingIndividually, setDownloadingIndividually] = useState(false);
  const [selectedFiles, setSelectedFiles] = useState<Set<string>>(new Set());
  const navigate = useNavigate();
  const { theme, setTheme } = useTheme();

  const currentPath = decodeURIComponent(subPath || '');
  const filteredFiles = getFilteredFiles(files, currentPath).sort((a, b) => {
    const nameA = a.path.split('/').pop() || '';
    const nameB = b.path.split('/').pop() || '';
    return smartCompare(nameA, nameB);
  });
  const subFolders = getSubFolders(files, currentPath).sort((a, b) => {
    const nameA = a || '';
    const nameB = b || '';
    return smartCompare(nameA, nameB);
  });

  const handleToggleFileSelection = (filePath: string) => {
    setSelectedFiles(prev => {
      const newSet = new Set(prev);
      newSet.has(filePath) ? newSet.delete(filePath) : newSet.add(filePath);
      return newSet;
    });
  };

  const handleToggleAllSelection = () => {
    const allPaths = filteredFiles.map(f => f.path);
    const allSelected = allPaths.every(p => selectedFiles.has(p));
    setSelectedFiles(allSelected ? new Set() : new Set(allPaths));
  };

  const handleDownloadAllAsZip = async () => {
    setDownloading(true);
    const zipEntries: Record<string, Uint8Array> = {};
    await Promise.all(
      files
        .filter(f => f.path.startsWith(currentPath) && f.status !== 'uploading')
        .map(async file => {
          try {
            const response = await fetch(file.url);
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

  const handleDownloadIndividually = async (filesToDownload: FileEntry[]) => {
    setDownloadingIndividually(true);
    for (const file of filesToDownload) {
      if (file.status === 'uploading') continue;
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

  const handleDownloadSelectedFiles = () => {
    handleDownloadIndividually(filteredFiles.filter(f => selectedFiles.has(f.path)));
  };

  const handleDownloadAllIndividually = () => {
    handleDownloadIndividually(filteredFiles);
  };

  const toggleTheme = () => setTheme(theme === 'dark' ? 'light' : 'dark');

  if (loading) {
    return (
      <div className="p-4 max-w-4xl mx-auto">
        <h1 className="text-4xl font-semibold mb-4">InstaShare</h1>
        <p>Loading folder contents...</p>
      </div>
    );
  }

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <h1 className="text-4xl font-semibold mb-4">InstaShare</h1>
      <div className="flex justify-between items-center mb-2 gap-2 flex-wrap">
        <h2 className="text-xl font-semibold">
          📁 {currentPath || folderId}
          <span className="text-base font-normal text-muted-foreground ml-2">
            ({formatBytes(getFolderSize(files, currentPath))})
          </span>
        </h2>
        <div className="flex gap-2 flex-wrap">
          <Button onClick={handleDownloadSelectedFiles} disabled={downloadingIndividually || selectedFiles.size === 0}>
            {downloadingIndividually ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Downloading...</span>
            ) : 'Download Selected'}
          </Button>
          <Button onClick={handleDownloadAllIndividually} disabled={downloadingIndividually}>
            {downloadingIndividually ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Downloading...</span>
            ) : 'Download Individually'}
          </Button>
          <Button onClick={handleDownloadAllAsZip} disabled={downloading || (filteredFiles.length === 1 && subFolders.length === 0)}>
            {downloading ? (
              <span className="flex items-center gap-2"><Loader className="animate-spin" size={16} /> Zipping...</span>
            ) : 'Download All as ZIP'}
          </Button>
          <div className="flex items-center gap-2 flex-wrap">
            <Button variant="ghost" size="icon" onClick={toggleTheme} title="Toggle Theme">
              {theme === 'dark' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
            </Button>
          </div>
        </div>
      </div>

      <Breadcrumbs folderId={folderId} currentPath={currentPath} />

      <div className="grid gap-4">
        {subFolders.map((folder, idx) => {
          const folderPrefix = (currentPath ? currentPath + '/' : '') + folder + '/';
          const hasUploading = files.some(f => f.path.startsWith(folderPrefix) && f.status === 'uploading');
          return (
            <FolderCard
              key={`folder-${idx}`}
              folder={folder}
              onClick={() => navigate(`/view/${folderId}/${encodeURIComponent((currentPath + '/' + folder).replace(/^\//, ''))}`)}
              itemCount={getItemCountInFolder(files, currentPath, folder)}
              size={getFolderSize(files, currentPath, folder)}
              uploading={hasUploading}
            />
          );
        })}

        {filteredFiles.length > 0 && (
          <label className="flex items-center gap-2 mb-2">
            <Checkbox
              checked={filteredFiles.every(f => selectedFiles.has(f.path))}
              onCheckedChange={handleToggleAllSelection}
            />
            <span className="text-sm">Select All</span>
          </label>
        )}

        {filteredFiles.map((file, index) => (
          <FileCard
            key={index}
            file={file}
            checked={selectedFiles.has(file.path)}
            onCheck={() => handleToggleFileSelection(file.path)}
          />
        ))}
      </div>
    </div>
  );
};

export default FolderViewer;