/// <reference types="svelte" />
/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL?: string;
}

declare module '*.svelte' {
  import type { Component } from 'svelte';

  const component: Component;
  export default component;
}
