import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// In dev, Vite serves the app on :5173 and forwards API and sign-in calls to the C# API.
// In production the API serves the built files itself, so there is one origin and no CORS.
const api = process.env.TASKLOOM_API ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [vue()],
  // Lets several dev servers share one checkout without fighting over the dependency cache.
  cacheDir: process.env.VITE_CACHE_DIR ?? 'node_modules/.vite',
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/auth': api,
      '/healthz': api,
    },
  },
  build: {
    outDir: '../src/Taskloom.Api/wwwroot',
    emptyOutDir: true,
  },
})
