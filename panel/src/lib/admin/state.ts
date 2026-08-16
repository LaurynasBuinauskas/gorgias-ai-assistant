// The admin page's machine, plain and testable like the panel's. The page holds no
// secrets of its own: the token unlocks it by proving itself against the API, and an
// unauthorized answer at any point locks it again with the reason on screen.
//
// One operation at a time is a server rule (the publish lock); the machine mirrors it so
// the page cannot even ask for a second one while the first runs.

import type {
  AdminDraft,
  PolicyDocument,
  PublishLedger,
  PublishStatus,
  ValidationFinding,
} from './api';

export type AdminData = {
  readonly drafts: readonly AdminDraft[];
  readonly documents: readonly PolicyDocument[];
  readonly markets: readonly string[];
  readonly history: readonly PublishLedger[];
};

/** A running or just-finished check, publish or rollback the page is following. */
export type Operation = {
  readonly kind: 'validate' | 'publish' | 'rollback';
  readonly publishId: string;
  readonly status: PublishStatus | null;
  readonly findings: readonly ValidationFinding[];
};

export type AdminState =
  | { readonly status: 'locked'; readonly message: string | null }
  | { readonly status: 'checking' }
  | { readonly status: 'ready'; readonly data: AdminData; readonly operation: Operation | null }
  | { readonly status: 'error'; readonly message: string };

export type AdminEvent =
  | { readonly type: 'unlock' }
  | { readonly type: 'loaded'; readonly data: AdminData }
  | {
      readonly type: 'operation_started';
      readonly kind: Operation['kind'];
      readonly publishId: string;
    }
  | {
      readonly type: 'operation_update';
      readonly status: PublishStatus;
      readonly findings: readonly ValidationFinding[];
    }
  | { readonly type: 'operation_dismissed' }
  | { readonly type: 'unauthorized'; readonly message: string }
  | { readonly type: 'failed'; readonly message: string }
  | { readonly type: 'signed_out' };

export const initialAdminState: AdminState = { status: 'locked', message: null };

/** True while the followed operation may still change — polling should continue. */
export function operationRunning(operation: Operation | null): boolean {
  return operation !== null && (operation.status === null || operation.status.state === 'running');
}

export function reduceAdmin(state: AdminState, event: AdminEvent): AdminState {
  switch (event.type) {
    case 'unlock':
      return state.status === 'locked' || state.status === 'error' ? { status: 'checking' } : state;

    case 'loaded':
      // A refresh keeps the operation card on screen; a first load has none to keep.
      return state.status === 'checking'
        ? { status: 'ready', data: event.data, operation: null }
        : state.status === 'ready'
          ? { ...state, data: event.data }
          : state;

    case 'operation_started':
      return state.status === 'ready' && !operationRunning(state.operation)
        ? {
            ...state,
            operation: {
              kind: event.kind,
              publishId: event.publishId,
              status: null,
              findings: [],
            },
          }
        : state;

    case 'operation_update':
      return state.status === 'ready' &&
        state.operation !== null &&
        state.operation.publishId === event.status.publishId
        ? {
            ...state,
            operation: { ...state.operation, status: event.status, findings: event.findings },
          }
        : state;

    case 'operation_dismissed':
      return state.status === 'ready' && !operationRunning(state.operation)
        ? { ...state, operation: null }
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
