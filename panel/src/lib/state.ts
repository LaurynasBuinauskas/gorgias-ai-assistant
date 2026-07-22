// The panel's state machine as a plain, testable module: a discriminated union plus a
// pure reducer with an exhaustive switch. Components are thin views over this.

export type PanelContext = { readonly ticketId: string; readonly account: string };

export type DraftView = {
  readonly draftId: string;
  readonly body: string;
  readonly language: string | null;
};

export type PanelState =
  | { readonly status: 'unauthenticated' }
  | { readonly status: 'idle'; readonly context: PanelContext }
  | { readonly status: 'generating'; readonly context: PanelContext }
  | { readonly status: 'drafted'; readonly context: PanelContext; readonly draft: DraftView }
  | {
      readonly status: 'insufficient_data';
      readonly context: PanelContext;
      readonly message: string;
    }
  | { readonly status: 'error'; readonly context: PanelContext; readonly message: string };

export type PanelEvent =
  // token present + first ticket ⇒ enter the draft lifecycle
  | { readonly type: 'authenticated'; readonly context: PanelContext }
  | { readonly type: 'signed_out' }
  | { readonly type: 'context'; readonly context: PanelContext }
  | { readonly type: 'generate' }
  | { readonly type: 'drafted'; readonly draft: DraftView }
  | { readonly type: 'insufficient'; readonly message: string }
  | { readonly type: 'failed'; readonly message: string };

export const initialState: PanelState = { status: 'unauthenticated' };

export function reduce(state: PanelState, event: PanelEvent): PanelState {
  switch (event.type) {
    case 'signed_out':
      return { status: 'unauthenticated' };

    case 'authenticated':
      return { status: 'idle', context: event.context };

    case 'context':
      // A new ticket resets the draft lifecycle (context_switch → idle).
      return state.status === 'unauthenticated'
        ? state
        : { status: 'idle', context: event.context };

    case 'generate':
      return state.status === 'idle' ||
        state.status === 'drafted' ||
        state.status === 'insufficient_data' ||
        state.status === 'error'
        ? { status: 'generating', context: state.context }
        : state;

    case 'drafted':
      return state.status === 'generating'
        ? { status: 'drafted', context: state.context, draft: event.draft }
        : state;

    case 'insufficient':
      return state.status === 'generating'
        ? { status: 'insufficient_data', context: state.context, message: event.message }
        : state;

    case 'failed':
      return state.status === 'generating'
        ? { status: 'error', context: state.context, message: event.message }
        : state;

    default:
      return assertNever(event);
  }
}

function assertNever(event: never): never {
  throw new Error(`Unhandled panel event: ${JSON.stringify(event)}`);
}
