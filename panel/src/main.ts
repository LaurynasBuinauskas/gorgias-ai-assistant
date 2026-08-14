import { mount } from 'svelte';
import AdminApp from './AdminApp.svelte';
import App from './App.svelte';

const target = document.getElementById('app');
if (!target) {
  throw new Error('Panel mount point #app not found in index.html');
}

// index.html paints a loading skeleton so the iframe is never blank white. `mount` appends
// rather than replaces, so clear it first — synchronously, so there is no flash.
target.replaceChildren();

// Same deployment, two front doors: the agent panel inside the Gorgias iframe, and the
// policy manager the client's workers open directly at #/admin. Hash routing, so the
// static host needs no route configuration and a reload lands on the same page.
mount(window.location.hash.startsWith('#/admin') ? AdminApp : App, { target });
