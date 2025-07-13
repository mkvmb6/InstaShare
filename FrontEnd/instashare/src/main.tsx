import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import FolderViewer from './FolderViewer.tsx'
import { ThemeProvider } from 'next-themes'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem>
      <BrowserRouter>
        <Routes>
          <Route path="/view/:folderId" element={<FolderViewer />} />
          <Route path="/view/:folderId/:subPath/*" element={<FolderViewer />} />
          <Route path="*" element={<p className="p-4">404 Not Found</p>} />
        </Routes>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
)
