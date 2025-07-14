export type FileStatus = 'uploading' | 'uploaded';

export interface FileEntry {
  path: string;
  url: string;
  size?: number;
  status: FileStatus;
}