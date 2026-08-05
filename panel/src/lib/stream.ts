// Server-sent events over fetch rather than EventSource: EventSource cannot send the
// Authorization header and cannot POST, and we need both (the conversation is replayed
// in the body on every turn).

import type { ChatTurn, Progress } from './state';

const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5249';

export type TicketInfo = {
  readonly customerName: string | null;
  readonly subject: string | null;
  readonly language: string | null;
  readonly messageCount: number;
};

/**
 * A source the draft leaned on. The backend has sent these on `done` since citations
 * existed; the panel discarded them, so an agent reviewing a draft could not see whether it
 * came from published policy or from someone else's ticket — which is the difference between
 * a fact they can rely on and a precedent they should check.
 */
export type Citation = {
  readonly label: string;
  readonly sourcePath: string;
  readonly market: string;
  /** Derived from `sourcePath`, because how far to trust it depends on which. */
  readonly kind: 'policy' | 'template' | 'ticket' | 'other';
};

export type StreamEvent =
  | { readonly kind: 'ticket'; readonly ticket: TicketInfo }
  | { readonly kind: 'progress'; readonly progress: Progress }
  | { readonly kind: 'delta'; readonly text: string }
  | { readonly kind: 'done'; readonly citations: readonly Citation[] }
  | { readonly kind: 'insufficient'; readonly message: string }
  | { readonly kind: 'error'; readonly message: string };

function citationKind(sourcePath: string): Citation['kind'] {
  if (sourcePath.startsWith('gorgias/ticket/')) return 'ticket';
  if (sourcePath.includes('/policy/')) return 'policy';
  if (sourcePath.includes('/templates/')) return 'template';
  return 'other';
}

/** Untrusted input: an API response is validated, never asserted. */
function parseCitations(value: unknown): Citation[] {
  if (!Array.isArray(value)) return [];

  const citations: Citation[] = [];
  for (const entry of value) {
    if (typeof entry !== 'object' || entry === null) continue;
    const record = entry as Record<string, unknown>;
    if (typeof record.sourcePath !== 'string' || typeof record.label !== 'string') continue;

    citations.push({
      label: record.label,
      sourcePath: record.sourcePath,
      market: typeof record.market === 'string' ? record.market : '',
      kind: citationKind(record.sourcePath),
    });
  }
  return citations;
}

export type DraftPayload = {
  readonly turns: readonly ChatTurn[];
  readonly instruction?: string;
};

/**
 * Streams a draft. Pass `signal` to cancel: an aborted run yields nothing further and
 * releases the connection, so the server stops generating tokens we would still pay for.
 */
export async function* streamDraft(
  token: string,
  ticketId: string,
  payload: DraftPayload,
  signal?: AbortSignal,
): AsyncGenerator<StreamEvent> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}/v1/tickets/${encodeURIComponent(ticketId)}/drafts/stream`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        v: 1,
        // Only the wire fields: an assistant turn also carries its panel-only progress
        // timeline, which the versioned request contract has no business receiving.
        turns: payload.turns.map(({ role, text }) => ({ role, text })),
        instruction: payload.instruction,
      }),
      ...(signal ? { signal } : {}),
    });
  } catch {
    // Cancelling is the caller's own doing, never something to report as a failure.
    if (!signal?.aborted) {
      yield { kind: 'error', message: 'Could not reach the assistant. Check your connection.' };
    }
    return;
  }

  if (response.status === 401) {
    yield { kind: 'error', message: 'Unauthorized — check your access token.' };
    return;
  }
  if (response.status === 404) {
    yield { kind: 'error', message: `Ticket ${ticketId} was not found.` };
    return;
  }
  if (response.status === 429) {
    yield { kind: 'error', message: 'Too many drafts in a row — wait a moment and try again.' };
    return;
  }
  if (!response.ok || !response.body) {
    yield { kind: 'error', message: `The assistant returned ${response.status}. Try again.` };
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // SSE frames are separated by a blank line; keep any partial frame in the buffer.
      let split = buffer.indexOf('\n\n');
      while (split !== -1) {
        const frame = buffer.slice(0, split);
        buffer = buffer.slice(split + 2);
        const event = parseFrame(frame);
        if (event) yield event;
        split = buffer.indexOf('\n\n');
      }
    }
  } catch {
    if (!signal?.aborted) {
      yield { kind: 'error', message: 'The connection to the assistant was lost.' };
    }
  } finally {
    // Runs on abort and when the caller stops consuming early, releasing the connection.
    await reader.cancel().catch(() => undefined);
  }
}

function parseFrame(frame: string): StreamEvent | null {
  let name = '';
  let data = '';
  for (const line of frame.split('\n')) {
    if (line.startsWith('event:')) name = line.slice(6).trim();
    else if (line.startsWith('data:')) data += line.slice(5).trim();
  }
  if (!name || !data) return null;

  let parsed: Record<string, unknown>;
  try {
    parsed = JSON.parse(data) as Record<string, unknown>;
  } catch {
    return null;
  }

  switch (name) {
    case 'ticket':
      return {
        kind: 'ticket',
        ticket: {
          customerName: typeof parsed.customerName === 'string' ? parsed.customerName : null,
          subject: typeof parsed.subject === 'string' ? parsed.subject : null,
          language: typeof parsed.language === 'string' ? parsed.language : null,
          messageCount: typeof parsed.messageCount === 'number' ? parsed.messageCount : 0,
        },
      };
    case 'progress':
      return parseProgress(parsed);
    case 'delta':
      return typeof parsed.text === 'string' ? { kind: 'delta', text: parsed.text } : null;
    case 'done':
      return { kind: 'done', citations: parseCitations(parsed.citations) };
    case 'insufficient':
      return {
        kind: 'insufficient',
        message: String(parsed.message ?? 'No draft could be generated.'),
      };
    case 'error':
      return { kind: 'error', message: String(parsed.message ?? 'Something went wrong.') };
    default:
      return null;
  }
}

/** An unknown stage or decision is dropped, not guessed at — same rule as unknown events. */
function parseProgress(parsed: Record<string, unknown>): StreamEvent | null {
  switch (parsed.stage) {
    case 'searched':
      return {
        kind: 'progress',
        progress: {
          stage: 'searched',
          market: typeof parsed.market === 'string' ? parsed.market : '',
          signal: typeof parsed.signal === 'string' ? parsed.signal : '',
          policy: count(parsed.policy),
          templates: count(parsed.templates),
          pastTickets: count(parsed.pastTickets),
          internalGuides: count(parsed.internalGuides),
        },
      };
    case 'coverage':
      return parsed.decision === 'passed' ||
        parsed.decision === 'declined' ||
        parsed.decision === 'skipped'
        ? { kind: 'progress', progress: { stage: 'coverage', decision: parsed.decision } }
        : null;
    case 'drafting':
      return { kind: 'progress', progress: { stage: 'drafting' } };
    default:
      return null;
  }
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
}
