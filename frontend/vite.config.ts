import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Build output goes to ../wwwroot, which the C# host serves to WebView2
// via SetVirtualHostNameToFolderMapping. base './' keeps asset paths relative.
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 2000,
  },
})
