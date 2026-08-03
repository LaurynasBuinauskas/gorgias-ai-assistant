import { defineConfig } from 'vitest/config';

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
  test: {
    // Unit tests only. Vitest's default glob would also claim `e2e/*.spec.ts`, which are
    // Playwright tests and fail on import — the two runners have to be told where the line is.
    include: ['src/**/*.test.ts'],
  },
});
