export type FileStatus = 'uploading' | 'uploaded' | 'failed';

export interface FileEntry {
  path: string;
  url: string;
  size?: number;
  status: FileStatus;
}