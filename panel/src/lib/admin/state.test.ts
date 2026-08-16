import { describe, expect, it } from 'vitest';
import type { PublishStatus } from './api';
import {
  type AdminData,
  type AdminState,
  initialAdminState,
  operationRunning,
  reduceAdmin,
} from './state';

const data: AdminData = {
  drafts: [],
  documents: [],
  markets: ['GLOBAL', 'DE'],
  history: [],
};

function status(step: string, state: PublishStatus['state']): PublishStatus {
  return { publishId: 'p1', step, state, detail: null, updatedAt: '2026-08-14T13:00:00Z' };
}

function ready(): AdminState {
  return reduceAdmin(reduceAdmin(initialAdminState, { type: 'unlock' }), { type: 'loaded', data });
}

describe('admin gate', () => {
  it('unlocks through checking into ready', () => {
    const state = ready();
    expect(state).toEqual({ status: 'ready', data, operation: null });
  });

  it('locks again from anywhere on unauthorized, with the reason', () => {
    const locked = reduceAdmin(ready(), { type: 'unauthorized', message: 'Token rotated.' });
    expect(locked).toEqual({ status: 'locked', message: 'Token rotated.' });
  });

  it('a failure shows the error and allows another unlock', () => {
    let state: AdminState = reduceAdmin(initialAdminState, { type: 'unlock' });
    state = reduceAdmin(state, { type: 'failed', message: 'API unreachable' });
    expect(state).toEqual({ status: 'error', message: 'API unreachable' });
    expect(reduceAdmin(state, { type: 'unlock' }).status).toBe('checking');
  });
});

describe('operations', () => {
  it('follows one operation from start through updates to done', () => {
    let state = reduceAdmin(ready(), {
      type: 'operation_started',
      kind: 'publish',
      publishId: 'p1',
    });
    expect(state.status === 'ready' && operationRunning(state.operation)).toBe(true);

    state = reduceAdmin(state, {
      type: 'operation_update',
      status: status('gating', 'running'),
      findings: [],
    });
    expect(state.status === 'ready' && state.operation?.status?.step).toBe('gating');

    state = reduceAdmin(state, {
      type: 'operation_update',
      status: status('published', 'succeeded'),
      findings: [],
    });
    expect(state.status === 'ready' && operationRunning(state.operation)).toBe(false);
  });

  it('refuses a second operation while one runs, and allows one after', () => {
    let state = reduceAdmin(ready(), {
      type: 'operation_started',
      kind: 'validate',
      publishId: 'p1',
    });
    state = reduceAdmin(state, {
      type: 'operation_started',
      kind: 'publish',
      publishId: 'p2',
    });
    expect(state.status === 'ready' && state.operation?.publishId).toBe('p1');

    state = reduceAdmin(state, {
      type: 'operation_update',
      status: status('validate-complete', 'succeeded'),
      findings: [],
    });
    state = reduceAdmin(state, {
      type: 'operation_started',
      kind: 'publish',
      publishId: 'p2',
    });
    expect(state.status === 'ready' && state.operation?.publishId).toBe('p2');
  });

  it('drops updates for an operation it is not following', () => {
    const state = reduceAdmin(ready(), {
      type: 'operation_started',
      kind: 'publish',
      publishId: 'p1',
    });
    const drifted = reduceAdmin(state, {
      type: 'operation_update',
      status: { ...status('published', 'succeeded'), publishId: 'other' },
      findings: [],
    });
    expect(drifted.status === 'ready' && drifted.operation?.status).toBeNull();
  });

  it('a running card cannot be dismissed; a finished one can', () => {
    let state = reduceAdmin(ready(), {
      type: 'operation_started',
      kind: 'publish',
      publishId: 'p1',
    });
    state = reduceAdmin(state, { type: 'operation_dismissed' });
    expect(state.status === 'ready' && state.operation?.publishId).toBe('p1');

    state = reduceAdmin(state, {
      type: 'operation_update',
      status: status('published', 'succeeded'),
      findings: [],
    });
    state = reduceAdmin(state, { type: 'operation_dismissed' });
    expect(state.status === 'ready' && state.operation).toBeNull();
  });

  it('a refresh keeps the operation card on screen', () => {
    let state = reduceAdmin(ready(), {
      type: 'operation_started',
      kind: 'publish',
      publishId: 'p1',
    });
    state = reduceAdmin(state, { type: 'loaded', data });
    expect(state.status === 'ready' && state.operation?.publishId).toBe('p1');
  });
});
