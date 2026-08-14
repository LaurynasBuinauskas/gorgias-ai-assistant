import { describe, expect, it } from 'vitest';
import type { AdminDraft } from './api';
import { type AdminState, initialAdminState, reduceAdmin } from './state';

const draft: AdminDraft = {
  blobName: 'DE/returns/a.md',
  fileName: 'a.md',
  market: 'DE',
  topic: 'returns',
  uploadedBy: 'Rasa',
  uploadedAt: '2026-08-14T12:00:00Z',
  sizeBytes: 100,
  state: 'staged',
  publishId: null,
  publishedAt: null,
};

describe('admin gate machine', () => {
  it('unlocks through checking into ready', () => {
    let state: AdminState = initialAdminState;
    state = reduceAdmin(state, { type: 'unlock' });
    expect(state.status).toBe('checking');
    state = reduceAdmin(state, { type: 'loaded', drafts: [draft] });
    expect(state).toEqual({ status: 'ready', drafts: [draft] });
  });

  it('locks again from anywhere on unauthorized, with the reason', () => {
    const ready = reduceAdmin(reduceAdmin(initialAdminState, { type: 'unlock' }), {
      type: 'loaded',
      drafts: [],
    });

    const locked = reduceAdmin(ready, { type: 'unauthorized', message: 'Token rotated.' });

    expect(locked).toEqual({ status: 'locked', message: 'Token rotated.' });
  });

  it('a failure shows the error and allows another unlock', () => {
    let state: AdminState = reduceAdmin(initialAdminState, { type: 'unlock' });
    state = reduceAdmin(state, { type: 'failed', message: 'API unreachable' });
    expect(state).toEqual({ status: 'error', message: 'API unreachable' });

    expect(reduceAdmin(state, { type: 'unlock' }).status).toBe('checking');
  });

  it('signing out clears the lock message', () => {
    const locked = reduceAdmin({ status: 'error', message: 'x' }, { type: 'signed_out' });

    expect(locked).toEqual({ status: 'locked', message: null });
  });
});
