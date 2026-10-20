export type FileStatus = 'uploading' | 'uploaded' | 'failed';

export interface FileEntry {
  path: string;
  url: string;
  size?: number;
  status: FileStatus;
}

/** Written by desktop app 2.0+. Older shares publish a bare array of files instead. */
export interface ShareIndex {
  name?: string;
  kind?: 'file' | 'folder';
  expiresUtc?: string;
  files: FileEntry[];
}

export type FileKind = 'image' | 'video' | 'audio' | 'pdf' | 'markdown' | 'text' | 'archive' | 'other';

/** A file or subfolder at the current level of the share. */
export type Item =
  | { type: 'folder'; name: string; path: string; count: number; size: number; uploading: number; failed: number }
  | { type: 'file'; name: string; path: string; kind: FileKind; entry: FileEntry };
