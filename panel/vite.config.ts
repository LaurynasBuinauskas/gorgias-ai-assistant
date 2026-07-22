import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';

// PORT is set by the Aspire AppHost (apphost/); standalone `pnpm dev` uses 5173.
const port = Number(process.env.PORT ?? 5173);

export default defineConfig({
  plugins: [svelte()],
  server: { port, strictPort: true },
});
