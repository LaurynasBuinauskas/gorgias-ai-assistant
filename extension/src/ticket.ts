// Ticket-ID detection from the URL only — never from page markup (principle #2).
// Verified against a live account: /app/views/{viewId}/{ticketId}. The /app/ticket/{id}
// form is kept as a defensive fallback for direct links.

const TICKET_PATTERNS: readonly RegExp[] = [/^\/app\/views\/\d+\/(\d+)/, /^\/app\/ticket\/(\d+)/];

export function ticketIdFromPath(pathname: string): string | null {
  for (const pattern of TICKET_PATTERNS) {
    const match = pattern.exec(pathname);
    if (match?.[1]) {
      return match[1];
    }
  }
  return null;
}

/**
 * Gorgias is a SPA, so navigation happens without page loads. We hook the history API,
 * listen for popstate, and keep a debounced MutationObserver as a backstop for
 * navigations the hooks miss. The callback only fires when the ticket actually changes.
 */
export function observeTicketChanges(onChange: (ticketId: string | null) => void): () => void {
  let current: string | null | undefined;

  const evaluate = () => {
    const next = ticketIdFromPath(window.location.pathname);
    if (next !== current) {
      current = next;
      onChange(next);
    }
  };

  const originalPushState = history.pushState.bind(history);
  const originalReplaceState = history.replaceState.bind(history);

  history.pushState = (...args: Parameters<History['pushState']>) => {
    originalPushState(...args);
    evaluate();
  };
  history.replaceState = (...args: Parameters<History['replaceState']>) => {
    originalReplaceState(...args);
    evaluate();
  };

  window.addEventListener('popstate', evaluate);

  let debounce: number | undefined;
  const observer = new MutationObserver(() => {
    window.clearTimeout(debounce);
    debounce = window.setTimeout(evaluate, 250);
  });
  observer.observe(document.body, { childList: true, subtree: true });

  evaluate();

  return () => {
    history.pushState = originalPushState;
    history.replaceState = originalReplaceState;
    window.removeEventListener('popstate', evaluate);
    observer.disconnect();
    window.clearTimeout(debounce);
  };
}
