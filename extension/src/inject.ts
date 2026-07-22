// MV3 content-script shell. Its entire job: detect the ticket ID from the URL, mount the
// panel iframe once, and postMessage context changes. No business logic, no credentials,
// no ticket content ever passes through here.

import { loadConfig, reportAnchorMode } from './config';
import { mountPanel } from './panel-frame';
import { observeTicketChanges } from './ticket';

async function start(): Promise<void> {
  const config = await loadConfig();
  if (config.killSwitch) {
    return;
  }

  const account = window.location.hostname.split('.')[0] ?? 'unknown';
  let panel: ReturnType<typeof mountPanel> | null = null;

  observeTicketChanges((ticketId) => {
    if (!ticketId) {
      panel?.setVisible(false);
      return;
    }

    if (!panel) {
      panel = mountPanel(config.panelOrigin, config.anchorProbes);
      reportAnchorMode(config.apiOrigin, account, panel.mode);
    }

    panel.setVisible(true);
    panel.sendContext(ticketId, account);
  });
}

void start();
