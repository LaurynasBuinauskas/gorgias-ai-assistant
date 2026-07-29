import { mount } from 'svelte';
import App from './App.svelte';

const target = document.getElementById('app');
if (!target) {
  throw new Error('Panel mount point #app not found in index.html');
}

// index.html paints a loading skeleton so the iframe is never blank white. `mount` appends
// rather than replaces, so clear it first — synchronously, so there is no flash.
target.replaceChildren();

mount(App, { target });
