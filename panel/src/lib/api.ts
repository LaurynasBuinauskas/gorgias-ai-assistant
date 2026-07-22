// Client for the Copilot API. Every fetch handles failure explicitly and narrows the
// two success shapes (drafted vs insufficient_data) by their `status` discriminator.

const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5249';

export type DraftOutcome =
  | {
      readonly kind: 'draft';
      readonly draftId: string;
      readonly body: string;
      readonly language: string | null;
    }
  | { readonly kind: 'insufficient'; readonly message: string }
  | { readonly kind: 'error'; readonly message: string };

export async function requestDraft(token: string, ticketId: string): Promise<DraftOutcome> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}/v1/tickets/${encodeURIComponent(ticketId)}/drafts`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    });
  } catch {
    return { kind: 'error', message: 'Could not reach the Copilot API. Is it running?' };
  }

  if (response.status === 401) {
    return { kind: 'error', message: 'Unauthorized — check your access token.' };
  }
  if (response.status === 404) {
    return { kind: 'error', message: `Ticket ${ticketId} was not found.` };
  }
  if (!response.ok) {
    return { kind: 'error', message: `The API returned ${response.status}. Try again.` };
  }

  const body = (await response.json()) as Record<string, unknown>;
  if (body.status === 'insufficient_data') {
    return {
      kind: 'insufficient',
      message: String(body.message ?? 'No draft could be generated.'),
    };
  }
  if (body.status === 'drafted') {
    return {
      kind: 'draft',
      draftId: String(body.draftId),
      body: String(body.body),
      language: typeof body.language === 'string' ? body.language : null,
    };
  }

  return { kind: 'error', message: 'Unexpected response from the API.' };
}
