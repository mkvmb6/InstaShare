import React from 'react';
import { useParams } from 'react-router-dom';
import FolderViewer from './FolderViewer';

const FolderViewerWrapper: React.FC = () => {
  const { folderId } = useParams();

  if (!folderId) {
    return <p className="p-4 text-red-500">Missing folder ID</p>;
  }

  return <FolderViewer folderId={folderId} />;
};

export default FolderViewerWrapper;
