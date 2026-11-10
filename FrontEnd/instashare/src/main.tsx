import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import FolderViewer, { Shell } from './FolderViewer.tsx'
import HomePage from './HomePage.tsx'
import { NotFound } from './components/custom/States.tsx'
import { ThemeProvider } from 'next-themes'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
      <BrowserRouter>
        <Routes>
          {/* The rest of the URL is the path inside the share, however deep. */}
          <Route path="/" element={<HomePage />} />
          <Route path="/view/:shareId/*" element={<FolderViewer />} />
          <Route path="*" element={<Shell><NotFound /></Shell>} />
        </Routes>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
)
