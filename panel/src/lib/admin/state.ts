// The admin page's gate machine, plain and testable like the panel's. The page holds no
// secrets of its own: the token unlocks it by proving itself against the API, and an
// unauthorized answer at any point locks it again with the reason on screen.

import type { AdminDraft } from './api';

export type AdminState =
  | { readonly status: 'locked'; readonly message: string | null }
  | { readonly status: 'checking' }
  | { readonly status: 'ready'; readonly drafts: readonly AdminDraft[] }
  | { readonly status: 'error'; readonly message: string };

export type AdminEvent =
  | { readonly type: 'unlock' }
  | { readonly type: 'loaded'; readonly drafts: readonly AdminDraft[] }
  | { readonly type: 'unauthorized'; readonly message: string }
  | { readonly type: 'failed'; readonly message: string }
  | { readonly type: 'signed_out' };

export const initialAdminState: AdminState = { status: 'locked', message: null };

export function reduceAdmin(state: AdminState, event: AdminEvent): AdminState {
  switch (event.type) {
    case 'unlock':
      return state.status === 'locked' || state.status === 'error' ? { status: 'checking' } : state;

    case 'loaded':
      return state.status === 'checking' || state.status === 'ready'
        ? { status: 'ready', drafts: event.drafts }
        : state;

    case 'unauthorized':
      // A bad token locks the page from anywhere — including mid-session, when the token
      // has been rotated server-side and every next call starts failing.
      return { status: 'locked', message: event.message };

    case 'failed':
      return state.status === 'checking' || state.status === 'ready'
        ? { status: 'error', message: event.message }
        : state;

    case 'signed_out':
      return { status: 'locked', message: null };

    default:
      return assertNever(event);
  }
}

function assertNever(event: never): never {
  throw new Error(`Unhandled admin event: ${JSON.stringify(event)}`);
}
