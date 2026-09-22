import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    // strictPort: start.ps1 waits on exactly this port, so a silent fallback to 7182 would hang it.
    port: 7181,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:7180',
    },
  },
})
