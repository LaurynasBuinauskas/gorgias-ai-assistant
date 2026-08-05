// The panel's state machine as a plain, testable module: a discriminated union plus a
// pure reducer with an exhaustive switch. Components are thin views over this.
//
// The panel owns the conversation (`turns`) and replays it to the stateless backend on
// every request, so a ticket switch or refresh simply starts a fresh conversation.

export type PanelContext = { readonly ticketId: string; readonly account: string };

/**
 * "assistant" = a draft the model produced, "agent" = an instruction the support agent gave.
 * An assistant turn keeps the `progress` of the run that produced it, so the how-it-was-made
 * timeline survives the draft's arrival instead of vanishing with the generating state.
 * Panel-only: the field is stripped before turns are replayed to the backend.
 */
export type ChatTurn = {
  readonly role: 'assistant' | 'agent';
  readonly text: string;
  readonly progress?: readonly Progress[];
};

/** Reading = fetching the ticket from Gorgias; writing = the model is producing tokens. */
export type GeneratePhase = 'reading' | 'writing';

/**
 * One step of the pipeline's work, streamed so the agent can watch it happen instead of
 * staring at a spinner. Counts only — the backend never streams retrieved text here.
 */
export type Progress =
  | {
      readonly stage: 'searched';
      readonly market: string;
      readonly signal: string;
      readonly policy: number;
      readonly templates: number;
      readonly pastTickets: number;
      readonly internalGuides: number;
    }
  | { readonly stage: 'coverage'; readonly decision: 'passed' | 'declined' | 'skipped' }
  | { readonly stage: 'drafting' };

export type PanelState =
  | { readonly status: 'unauthenticated' }
  | { readonly status: 'idle'; readonly context: PanelContext; readonly turns: readonly ChatTurn[] }
  | {
      readonly status: 'generating';
      readonly context: PanelContext;
      readonly turns: readonly ChatTurn[];
      readonly phase: GeneratePhase;
      readonly partial: string;
      readonly progress: readonly Progress[];
    }
  | {
      readonly status: 'insufficient_data';
      readonly context: PanelContext;
      readonly turns: readonly ChatTurn[];
      readonly message: string;
      /** Carried from the run so a decline can show what was searched before it refused. */
      readonly progress: readonly Progress[];
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
  | { readonly type: 'writing' }
  | { readonly type: 'progress'; readonly progress: Progress }
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
      return {
        status: 'generating',
        context: state.context,
        turns,
        phase: 'reading',
        partial: '',
        progress: [],
      };
    }

    case 'writing':
      return state.status === 'generating' ? { ...state, phase: 'writing' } : state;

    case 'progress':
      return state.status === 'generating'
        ? { ...state, progress: [...state.progress, event.progress] }
        : state;

    case 'delta':
      // A token implies the fetch is done, even if the 'writing' event was missed.
      return state.status === 'generating'
        ? { ...state, phase: 'writing', partial: state.partial + event.text }
        : state;

    case 'completed': {
      if (state.status !== 'generating') return state;
      const text = state.partial.trim();
      if (text.length === 0) {
        return { status: 'idle', context: state.context, turns: state.turns };
      }
      const turn: ChatTurn =
        state.progress.length > 0
          ? { role: 'assistant', text, progress: state.progress }
          : { role: 'assistant', text };
      return { status: 'idle', context: state.context, turns: [...state.turns, turn] };
    }

    case 'insufficient':
      return state.status === 'generating'
        ? {
            status: 'insufficient_data',
            context: state.context,
            turns: state.turns,
            message: event.message,
            progress: state.progress,
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
