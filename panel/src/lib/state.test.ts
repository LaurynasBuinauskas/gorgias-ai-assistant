import { describe, expect, it } from 'vitest';
import { initialState, type PanelContext, type PanelState, reduce } from './state';

const context: PanelContext = { ticketId: '271859246', account: 'timeresistance' };
const draft = { draftId: 'd1', body: 'Hallo', language: 'de' };

describe('panel state machine', () => {
  it('starts unauthenticated', () => {
    expect(initialState.status).toBe('unauthenticated');
  });

  it('authenticates into idle with a context', () => {
    const state = reduce(initialState, { type: 'authenticated', context });
    expect(state).toEqual({ status: 'idle', context });
  });

  it('ignores context while unauthenticated', () => {
    expect(reduce(initialState, { type: 'context', context })).toEqual(initialState);
  });

  it('runs the happy path idle → generating → drafted', () => {
    let state: PanelState = reduce(initialState, { type: 'authenticated', context });
    state = reduce(state, { type: 'generate' });
    expect(state.status).toBe('generating');
    state = reduce(state, { type: 'drafted', draft });
    expect(state).toEqual({ status: 'drafted', context, draft });
  });

  it('surfaces insufficient_data and error only from generating', () => {
    const idle = reduce(initialState, { type: 'authenticated', context });
    const generating = reduce(idle, { type: 'generate' });
    expect(reduce(generating, { type: 'insufficient', message: 'no knowledge' }).status).toBe(
      'insufficient_data',
    );
    expect(reduce(generating, { type: 'failed', message: 'boom' }).status).toBe('error');
    // A stray 'drafted' while idle is ignored (no illegal transition).
    expect(reduce(idle, { type: 'drafted', draft })).toEqual(idle);
  });

  it('regenerates from a drafted state', () => {
    let state: PanelState = reduce(initialState, { type: 'authenticated', context });
    state = reduce(state, { type: 'generate' });
    state = reduce(state, { type: 'drafted', draft });
    expect(reduce(state, { type: 'generate' }).status).toBe('generating');
  });

  it('switches ticket back to idle and signs out', () => {
    let state: PanelState = reduce(initialState, { type: 'authenticated', context });
    const other = { ticketId: '999', account: 'timeresistance' };
    state = reduce(state, { type: 'context', context: other });
    expect(state).toEqual({ status: 'idle', context: other });
    expect(reduce(state, { type: 'signed_out' }).status).toBe('unauthenticated');
  });
});
