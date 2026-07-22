import { mount } from 'svelte';
import App from './App.svelte';

const target = document.getElementById('app');
if (!target) {
  throw new Error('Panel mount point #app not found in index.html');
}

mount(App, { target });
