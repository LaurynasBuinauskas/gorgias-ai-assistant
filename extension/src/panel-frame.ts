// Owns the single persistent iframe. The frame is created once and reused for the
// lifetime of the page — a ticket change is a postMessage, never a re-mount, so the
// panel keeps its session and in-flight state.

const FRAME_ID = 'copilot-panel-frame';
const TOGGLE_ID = 'copilot-panel-toggle';

export type PanelFrame = {
  readonly mode: 'docked' | 'floating';
  sendContext(ticketId: string, account: string): void;
  setVisible(visible: boolean): void;
};

function findAnchor(probes: readonly string[]): Element | null {
  for (const probe of probes) {
    const anchor = document.querySelector(probe);
    if (anchor) return anchor;
  }
  return null;
}

export function mountPanel(panelOrigin: string, anchorProbes: readonly string[]): PanelFrame {
  const frame = document.createElement('iframe');
  frame.id = FRAME_ID;
  frame.title = 'Gorgias AI Copilot';
  frame.allow = 'clipboard-write';
  // The panel pins its postMessage origin to whoever framed it.
  frame.src = `${panelOrigin}/?shellOrigin=${encodeURIComponent(window.location.origin)}`;

  const anchor = findAnchor(anchorProbes);
  const mode: 'docked' | 'floating' = anchor ? 'docked' : 'floating';
  frame.classList.add(mode === 'docked' ? 'copilot-docked' : 'copilot-floating');
  (anchor ?? document.body).appendChild(frame);

  let pending: { ticketId: string; account: string } | null = null;
  let ready = false;

  window.addEventListener('message', (event) => {
    if (event.origin !== panelOrigin) return;
    const data = event.data as { v?: unknown; type?: unknown } | null;
    if (data?.v !== 1 || data.type !== 'copilot:ready') return;

    ready = true;
    if (pending) {
      post(pending.ticketId, pending.account);
    }
  });

  function post(ticketId: string, account: string) {
    frame.contentWindow?.postMessage(
      { v: 1, type: 'copilot:context', ticketId, account },
      panelOrigin,
    );
  }

  const panel: PanelFrame = {
    mode,
    sendContext(ticketId, account) {
      pending = { ticketId, account };
      if (ready) post(ticketId, account);
    },
    setVisible(visible) {
      frame.classList.toggle('copilot-hidden', !visible);
      toggle.classList.toggle('copilot-collapsed', !visible);
      toggle.textContent = visible ? 'Hide Copilot' : 'Copilot';
    },
  };

  // Floating mode overlays the page, so the agent needs a way to get it out of the way.
  const toggle = document.createElement('button');
  toggle.id = TOGGLE_ID;
  toggle.type = 'button';
  toggle.textContent = 'Hide Copilot';
  toggle.addEventListener('click', () =>
    panel.setVisible(frame.classList.contains('copilot-hidden')),
  );
  if (mode === 'floating') {
    document.body.appendChild(toggle);
  }

  return panel;
}
