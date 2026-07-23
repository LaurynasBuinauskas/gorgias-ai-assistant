/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origin the panel SPA is served from; falls back to the local dev server. */
  readonly VITE_PANEL_ORIGIN?: string;
  /** Origin of the Copilot API; falls back to the local dev server. */
  readonly VITE_API_ORIGIN?: string;
}
