import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The backend URL for local development. Override with VITE_API_PROXY if it runs elsewhere
// (e.g. http://localhost:8080 when the API runs in Docker).
const apiTarget = process.env.VITE_API_PROXY ?? 'http://localhost:5283'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
    },
  },
})
