// Server-sent events over fetch rather than EventSource: EventSource cannot send the
// Authorization header and cannot POST, and we need both (the conversation is replayed
// in the body on every turn).

import type { ChatTurn } from './state';

const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5249';

export type TicketInfo = {
  readonly customerName: string | null;
  readonly subject: string | null;
  readonly language: string | null;
  readonly messageCount: number;
};

export type StreamEvent =
  | { readonly kind: 'ticket'; readonly ticket: TicketInfo }
  | { readonly kind: 'delta'; readonly text: string }
  | { readonly kind: 'done' }
  | { readonly kind: 'insufficient'; readonly message: string }
  | { readonly kind: 'error'; readonly message: string };

export type DraftPayload = {
  readonly turns: readonly ChatTurn[];
  readonly instruction?: string;
};

export async function* streamDraft(
  token: string,
  ticketId: string,
  payload: DraftPayload,
): AsyncGenerator<StreamEvent> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}/v1/tickets/${encodeURIComponent(ticketId)}/drafts/stream`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ v: 1, turns: payload.turns, instruction: payload.instruction }),
    });
  } catch {
    yield { kind: 'error', message: 'Could not reach the assistant. Check your connection.' };
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
  if (!response.ok || !response.body) {
    yield { kind: 'error', message: `The assistant returned ${response.status}. Try again.` };
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

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
    case 'delta':
      return typeof parsed.text === 'string' ? { kind: 'delta', text: parsed.text } : null;
    case 'done':
      return { kind: 'done' };
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
