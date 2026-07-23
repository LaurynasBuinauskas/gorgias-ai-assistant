// The panel's state machine as a plain, testable module: a discriminated union plus a
// pure reducer with an exhaustive switch. Components are thin views over this.
//
// The panel owns the conversation (`turns`) and replays it to the stateless backend on
// every request, so a ticket switch or refresh simply starts a fresh conversation.

export type PanelContext = { readonly ticketId: string; readonly account: string };

/** "assistant" = a draft the model produced, "agent" = an instruction the support agent gave. */
export type ChatTurn = { readonly role: 'assistant' | 'agent'; readonly text: string };

export type PanelState =
  | { readonly status: 'unauthenticated' }
  | { readonly status: 'idle'; readonly context: PanelContext; readonly turns: readonly ChatTurn[] }
  | {
      readonly status: 'generating';
      readonly context: PanelContext;
      readonly turns: readonly ChatTurn[];
      readonly partial: string;
    }
  | {
      readonly status: 'insufficient_data';
      readonly context: PanelContext;
      readonly turns: readonly ChatTurn[];
      readonly message: string;
    }
  | {
      readonly status: 'error';
      readonly context: PanelContext;
      readonly turns: readonly ChatTurn[];
      readonly message: string;
    };

export type PanelEvent =
  | { readonly type: 'authenticated'; readonly context: PanelContext }
  | { readonly type: 'signed_out' }
  | { readonly type: 'context'; readonly context: PanelContext }
  | { readonly type: 'generate'; readonly instruction?: string }
  | { readonly type: 'delta'; readonly text: string }
  | { readonly type: 'completed' }
  | { readonly type: 'insufficient'; readonly message: string }
  | { readonly type: 'failed'; readonly message: string };

export const initialState: PanelState = { status: 'unauthenticated' };

/** States the agent can start a new generation from. */
function canGenerate(
  state: PanelState,
): state is Extract<PanelState, { status: 'idle' | 'insufficient_data' | 'error' }> {
  return (
    state.status === 'idle' || state.status === 'insufficient_data' || state.status === 'error'
  );
}

export function reduce(state: PanelState, event: PanelEvent): PanelState {
  switch (event.type) {
    case 'signed_out':
      return { status: 'unauthenticated' };

    case 'authenticated':
      return { status: 'idle', context: event.context, turns: [] };

    case 'context':
      // A new ticket is a new conversation.
      return state.status === 'unauthenticated'
        ? state
        : { status: 'idle', context: event.context, turns: [] };

    case 'generate': {
      if (!canGenerate(state)) return state;
      const turns = event.instruction
        ? [...state.turns, { role: 'agent', text: event.instruction } as const]
        : state.turns;
      return { status: 'generating', context: state.context, turns, partial: '' };
    }

    case 'delta':
      return state.status === 'generating'
        ? { ...state, partial: state.partial + event.text }
        : state;

    case 'completed': {
      if (state.status !== 'generating') return state;
      const text = state.partial.trim();
      const turns =
        text.length > 0 ? [...state.turns, { role: 'assistant', text } as const] : state.turns;
      return { status: 'idle', context: state.context, turns };
    }

    case 'insufficient':
      return state.status === 'generating'
        ? {
            status: 'insufficient_data',
            context: state.context,
            turns: state.turns,
            message: event.message,
          }
        : state;

    case 'failed':
      return state.status === 'generating'
        ? { status: 'error', context: state.context, turns: state.turns, message: event.message }
        : state;

    default:
      return assertNever(event);
  }
}

function assertNever(event: never): never {
  throw new Error(`Unhandled panel event: ${JSON.stringify(event)}`);
}
