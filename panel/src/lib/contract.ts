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

/**
 * The shell passes its origin as a query parameter when it builds the iframe src, so the
 * panel can pin messaging to exactly one origin in both directions (never `*`). Only
 * Gorgias and loopback origins are accepted; production also enforces
 * `Content-Security-Policy: frame-ancestors https://*.gorgias.com`.
 * Falls back to same-origin, which is what the dev harness uses.
 */
export function resolveShellOrigin(): string {
  const param = new URLSearchParams(window.location.search).get('shellOrigin');
  if (param) {
    try {
      const url = new URL(param);
      const isGorgias =
        url.protocol === 'https:' &&
        (url.hostname === 'gorgias.com' || url.hostname.endsWith('.gorgias.com'));
      const isLoopback = url.hostname === 'localhost' || url.hostname === '127.0.0.1';
      if (isGorgias || isLoopback) {
        return url.origin;
      }
    } catch {
      // Malformed parameter — fall through to same-origin.
    }
  }
  return window.location.origin;
}

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
