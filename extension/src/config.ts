// Shell configuration. Defaults are baked in; a storage override points the panel at a
// local dev server; the backend supplies the kill switch and anchor selectors so fixes
// ship without an extension release.

export type ShellConfig = {
  readonly panelOrigin: string;
  readonly apiOrigin: string;
  readonly killSwitch: boolean;
  readonly anchorProbes: readonly string[];
};

const DEFAULTS = {
  panelOrigin: 'http://localhost:5173',
  apiOrigin: 'http://localhost:5249',
} as const;

type Origins = { panelOrigin: string; apiOrigin: string };

async function readOrigins(): Promise<Origins> {
  try {
    const stored = await chrome.storage.local.get(['panelOrigin', 'apiOrigin']);
    return {
      panelOrigin:
        typeof stored.panelOrigin === 'string' ? stored.panelOrigin : DEFAULTS.panelOrigin,
      apiOrigin: typeof stored.apiOrigin === 'string' ? stored.apiOrigin : DEFAULTS.apiOrigin,
    };
  } catch {
    return { ...DEFAULTS };
  }
}

/** Backend config is best-effort: if it can't be reached the shell still mounts. */
async function readRemoteConfig(
  apiOrigin: string,
): Promise<{ killSwitch: boolean; anchorProbes: string[] }> {
  try {
    const response = await fetch(`${apiOrigin}/v1/config`);
    if (!response.ok) return { killSwitch: false, anchorProbes: [] };

    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return { killSwitch: false, anchorProbes: [] };

    const config = body as Record<string, unknown>;
    return {
      killSwitch: config.killSwitch === true,
      anchorProbes: Array.isArray(config.anchorProbes)
        ? config.anchorProbes.filter((probe): probe is string => typeof probe === 'string')
        : [],
    };
  } catch {
    return { killSwitch: false, anchorProbes: [] };
  }
}

export async function loadConfig(): Promise<ShellConfig> {
  const { panelOrigin, apiOrigin } = await readOrigins();
  const remote = await readRemoteConfig(apiOrigin);

  return {
    panelOrigin,
    apiOrigin,
    killSwitch: remote.killSwitch,
    anchorProbes: remote.anchorProbes,
  };
}

export function reportAnchorMode(
  apiOrigin: string,
  account: string,
  mode: 'docked' | 'floating',
): void {
  void fetch(`${apiOrigin}/v1/telemetry/anchor`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ v: 1, account, mode }),
  }).catch(() => {
    // Telemetry is best-effort; never disturb the agent's page.
  });
}
