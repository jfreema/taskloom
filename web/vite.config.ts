import { readdir, readFile, writeFile } from 'node:fs/promises'
import { extname, join, resolve } from 'node:path'
import { promisify } from 'node:util'
import { brotliCompress, constants, gzip } from 'node:zlib'
import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

// In dev, Vite serves the app on :5173 and forwards API and sign-in calls to the C# API.
// In production the API serves the built files itself, so there is one origin and no CORS.
const api = process.env.TASKLOOM_API ?? 'http://localhost:5080'

/** The text files of a build; fonts and images are compressed already. */
const COMPRESSIBLE = new Set(['.js', '.mjs', '.css', '.html', '.svg', '.json', '.txt', '.xml', '.map', '.webmanifest'])

/**
 * Efficiency 1: once the build is written, puts a Brotli (.br) and a gzip (.gz) copy beside each text file, compressed
 * as small as they go. Doing it here, once, gives files about 13% smaller than the server could afford to make on
 * every request. The API sends a copy to each browser that accepts it (src/Taskloom.Api/AppFiles.cs).
 */
function precompress(): Plugin {
  const brotli = promisify(brotliCompress)
  const gz = promisify(gzip)
  let outDir = ''
  return {
    name: 'taskloom-precompress',
    apply: 'build',
    configResolved(config) {
      outDir = resolve(config.root, config.build.outDir)
    },
    async closeBundle() {
      const entries = await readdir(outDir, { recursive: true, withFileTypes: true })
      const files = entries
        .filter((e) => e.isFile() && COMPRESSIBLE.has(extname(e.name)))
        .map((e) => join(e.parentPath, e.name))
      await Promise.all(
        files.map(async (file) => {
          const data = await readFile(file)
          const [br, gzipped] = await Promise.all([
            brotli(data, {
              params: {
                [constants.BROTLI_PARAM_MODE]: constants.BROTLI_MODE_TEXT,
                [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
                [constants.BROTLI_PARAM_SIZE_HINT]: data.length,
              },
            }),
            gz(data, { level: constants.Z_BEST_COMPRESSION }),
          ])
          await Promise.all([writeFile(`${file}.br`, br), writeFile(`${file}.gz`, gzipped)])
        }),
      )
    },
  }
}

export default defineConfig({
  plugins: [vue(), precompress()],
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
