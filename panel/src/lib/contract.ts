// Shell ↔ panel postMessage contract. Versioned, append-only, origin-pinned.
// The shell (or the dev harness) sends copilot:context; the panel replies copilot:ready.

export type CopilotContext = {
  readonly v: 1;
  readonly type: 'copilot:context';
  readonly ticketId: string;
  readonly account: string;
};

export type CopilotReady = {
  readonly v: 1;
  readonly type: 'copilot:ready';
};

export const copilotReady: CopilotReady = { v: 1, type: 'copilot:ready' };

/** Runtime validation — postMessage data is untrusted; a type assertion is not validation. */
export function parseCopilotContext(data: unknown): CopilotContext | null {
  if (typeof data !== 'object' || data === null) {
    return null;
  }

  const message = data as Record<string, unknown>;
  if (
    message.v === 1 &&
    message.type === 'copilot:context' &&
    typeof message.ticketId === 'string' &&
    message.ticketId.length > 0 &&
    typeof message.account === 'string'
  ) {
    return { v: 1, type: 'copilot:context', ticketId: message.ticketId, account: message.account };
  }

  return null;
}
