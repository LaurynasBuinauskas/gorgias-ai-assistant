import { defineConfig } from 'vite';

// The shell is a single MV3 content script: one IIFE bundle, stable file name,
// no hashing (manifest.json references it by exact path).
export default defineConfig({
  build: {
    lib: {
      entry: 'src/inject.ts',
      formats: ['iife'],
      name: 'copilotShell',
      fileName: () => 'inject.js',
    },
    outDir: 'dist',
  },
});
