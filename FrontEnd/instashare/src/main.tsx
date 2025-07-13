import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import FolderViewerWrapper from './FolderViewerWrapper.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route path="/view/:folderId" element={<FolderViewerWrapper/>} />
        <Route path="*" element={<p className="p-4">404 Not Found</p>} />
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
