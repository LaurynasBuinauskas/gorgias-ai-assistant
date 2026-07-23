import { describe, expect, it } from 'vitest';
import { initialState, type PanelContext, type PanelState, reduce } from './state';

const context: PanelContext = { ticketId: '271859246', account: 'timeresistance' };

/** Drive the machine to a completed draft, the state most interactions start from. */
function drafted(text = 'Hallo Jane'): PanelState {
  let state: PanelState = reduce(initialState, { type: 'authenticated', context });
  state = reduce(state, { type: 'generate' });
  state = reduce(state, { type: 'delta', text });
  return reduce(state, { type: 'completed' });
}

describe('generate phase', () => {
  it('starts in reading, then flips to writing', () => {
    const reading = reduce(reduce(initialState, { type: 'authenticated', context }), {
      type: 'generate',
    });
    expect(reading).toMatchObject({ status: 'generating', phase: 'reading', partial: '' });
    expect(reduce(reading, { type: 'writing' })).toMatchObject({ phase: 'writing' });
  });

  it('a delta implies writing even if the writing event was missed', () => {
    const reading = reduce(reduce(initialState, { type: 'authenticated', context }), {
      type: 'generate',
    });
    expect(reduce(reading, { type: 'delta', text: 'x' })).toMatchObject({
      phase: 'writing',
      partial: 'x',
    });
  });
});

describe('panel state machine', () => {
  it('starts unauthenticated', () => {
    expect(initialState.status).toBe('unauthenticated');
  });

  it('authenticates into an empty conversation', () => {
    expect(reduce(initialState, { type: 'authenticated', context })).toEqual({
      status: 'idle',
      context,
      turns: [],
    });
  });

  it('ignores context while unauthenticated', () => {
    expect(reduce(initialState, { type: 'context', context })).toEqual(initialState);
  });

  it('accumulates streamed deltas into a draft turn', () => {
    let state: PanelState = reduce(initialState, { type: 'authenticated', context });
    state = reduce(state, { type: 'generate' });
    state = reduce(state, { type: 'delta', text: 'Hallo ' });
    state = reduce(state, { type: 'delta', text: 'Jane' });
    expect(state).toMatchObject({ status: 'generating', partial: 'Hallo Jane' });

    state = reduce(state, { type: 'completed' });
    expect(state).toEqual({
      status: 'idle',
      context,
      turns: [{ role: 'assistant', text: 'Hallo Jane' }],
    });
  });

  it('records the instruction before streaming the revision', () => {
    const state = reduce(drafted(), { type: 'generate', instruction: 'translate to English' });
    expect(state).toMatchObject({ status: 'generating', partial: '' });
    expect(state.status !== 'unauthenticated' && state.turns).toEqual([
      { role: 'assistant', text: 'Hallo Jane' },
      { role: 'agent', text: 'translate to English' },
    ]);
  });

  it('keeps the conversation when a generation fails', () => {
    const state = reduce(reduce(drafted(), { type: 'generate', instruction: 'shorter' }), {
      type: 'failed',
      message: 'boom',
    });
    expect(state.status).toBe('error');
    expect(state.status !== 'unauthenticated' && state.turns).toHaveLength(2);
  });

  it('can retry from error and insufficient_data', () => {
    const failed = reduce(reduce(drafted(), { type: 'generate' }), {
      type: 'failed',
      message: 'x',
    });
    expect(reduce(failed, { type: 'generate' }).status).toBe('generating');

    const thin = reduce(reduce(drafted(), { type: 'generate' }), {
      type: 'insufficient',
      message: 'no customer message',
    });
    expect(reduce(thin, { type: 'generate' }).status).toBe('generating');
  });

  it('ignores deltas and completion outside generating', () => {
    const idle = drafted();
    expect(reduce(idle, { type: 'delta', text: 'stray' })).toEqual(idle);
    expect(reduce(idle, { type: 'completed' })).toEqual(idle);
  });

  it('drops an empty draft rather than adding a blank turn', () => {
    let state: PanelState = reduce(initialState, { type: 'authenticated', context });
    state = reduce(state, { type: 'generate' });
    state = reduce(state, { type: 'completed' });
    expect(state).toEqual({ status: 'idle', context, turns: [] });
  });

  it('switching ticket starts a fresh conversation, and sign-out resets', () => {
    const other = { ticketId: '999', account: 'timeresistance' };
    const switched = reduce(drafted(), { type: 'context', context: other });
    expect(switched).toEqual({ status: 'idle', context: other, turns: [] });
    expect(reduce(switched, { type: 'signed_out' }).status).toBe('unauthenticated');
  });
});
