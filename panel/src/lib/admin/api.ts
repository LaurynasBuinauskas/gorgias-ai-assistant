// Client for the admin policy API. Untrusted input rules apply to our own backend too:
// every response is validated at runtime before the UI renders it, and malformed list
// entries are dropped rather than crashing the page a client worker is relying on.

const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5249';

export type ApiFailure = {
  readonly ok: false;
  readonly kind: 'unauthorized' | 'network' | 'refused' | 'unexpected';
  readonly message: string;
};

export type ApiResult<T> = { readonly ok: true; readonly value: T } | ApiFailure;

export type AdminDraft = {
  readonly blobName: string;
  readonly fileName: string;
  readonly market: string;
  readonly topic: string;
  readonly uploadedBy: string;
  readonly uploadedAt: string;
  readonly sizeBytes: number;
  readonly state: 'staged' | 'published';
  readonly publishId: string | null;
  readonly publishedAt: string | null;
};

export type PublishStatus = {
  readonly publishId: string;
  readonly step: string;
  readonly state: 'running' | 'succeeded' | 'failed';
  readonly detail: unknown;
  readonly updatedAt: string;
};

export type ValidationFinding = {
  readonly blobName: string;
  readonly kind: string;
  readonly message: string;
};

export type PublishLedger = {
  readonly publishId: string;
  readonly mode: string;
  readonly publishedBy: string;
  readonly publishedAt: string;
  readonly blobs: readonly string[];
  readonly snapshotIndex: string;
};

export type PolicyDocument = {
  readonly sourcePath: string;
  readonly market: string;
  readonly topic: string;
  readonly chunks: number;
};

export type CurrentPolicy = {
  readonly markets: readonly string[];
  readonly documents: readonly PolicyDocument[];
};

export async function listFiles(token: string): Promise<ApiResult<AdminDraft[]>> {
  const result = await request(token, '/v1/admin/policy/files');
  if (!result.ok) return result;
  const drafts = asRecord(result.value)?.drafts;
  if (!Array.isArray(drafts)) {
    return unexpected('The drafts list came back in an unknown shape.');
  }
  return { ok: true, value: drafts.map(parseDraft).filter((d): d is AdminDraft => d !== null) };
}

export async function uploadFile(
  token: string,
  file: File,
  market: string,
  topic: string,
  uploadedBy: string,
): Promise<ApiResult<AdminDraft>> {
  const form = new FormData();
  form.append('file', file);
  form.append('market', market);
  form.append('topic', topic);
  form.append('uploadedBy', uploadedBy);
  const result = await request(token, '/v1/admin/policy/files', { method: 'POST', body: form });
  if (!result.ok) return result;
  const draft = parseDraft(result.value);
  return draft ? { ok: true, value: draft } : unexpected('The upload reply was unreadable.');
}

export async function getCurrent(token: string): Promise<ApiResult<CurrentPolicy>> {
  const result = await request(token, '/v1/admin/policy/current');
  if (!result.ok) return result;
  const record = asRecord(result.value);
  const markets = Array.isArray(record?.markets)
    ? record.markets.filter((m): m is string => typeof m === 'string')
    : [];
  const rows = record?.documents;
  if (!Array.isArray(rows)) {
    return unexpected('The current-policy list came back in an unknown shape.');
  }
  return {
    ok: true,
    value: {
      markets,
      documents: rows.flatMap((entry) => {
        const document = asRecord(entry);
        return document &&
          typeof document.sourcePath === 'string' &&
          typeof document.market === 'string'
          ? [
              {
                sourcePath: document.sourcePath,
                market: document.market,
                topic: typeof document.topic === 'string' ? document.topic : '',
                chunks: typeof document.chunks === 'number' ? document.chunks : 0,
              },
            ]
          : [];
      }),
    },
  };
}

export async function getFileContent(token: string, blobName: string): Promise<ApiResult<string>> {
  const result = await request(
    token,
    `/v1/admin/policy/file-content?blob=${encodeURIComponent(blobName)}`,
  );
  if (!result.ok) return result;
  const content = asRecord(result.value)?.content;
  return typeof content === 'string'
    ? { ok: true, value: content }
    : unexpected('The file content was unreadable.');
}

export async function startValidate(
  token: string,
  blobs: readonly string[],
  requestedBy: string,
): Promise<ApiResult<string>> {
  const result = await request(token, '/v1/admin/policy/validate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ v: 1, blobs, publishedBy: requestedBy }),
  });
  if (!result.ok) return result;
  const publishId = asRecord(result.value)?.publishId;
  return typeof publishId === 'string'
    ? { ok: true, value: publishId }
    : unexpected('The check reply carried no id.');
}

export async function startPublish(
  token: string,
  blobs: readonly string[],
  publishedBy: string,
): Promise<ApiResult<string>> {
  const result = await request(token, '/v1/admin/policy/publish', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ v: 1, blobs, publishedBy }),
  });
  if (!result.ok) return result;
  const publishId = asRecord(result.value)?.publishId;
  return typeof publishId === 'string'
    ? { ok: true, value: publishId }
    : unexpected('The publish reply carried no id.');
}

export async function startRollback(
  token: string,
  publishedBy: string,
): Promise<ApiResult<string>> {
  const result = await request(token, '/v1/admin/policy/rollback', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ v: 1, publishedBy }),
  });
  if (!result.ok) return result;
  const publishId = asRecord(result.value)?.publishId;
  return typeof publishId === 'string'
    ? { ok: true, value: publishId }
    : unexpected('The rollback reply carried no id.');
}

export async function getPublish(
  token: string,
  publishId: string,
): Promise<ApiResult<{ status: PublishStatus; findings: ValidationFinding[] }>> {
  const result = await request(
    token,
    `/v1/admin/policy/publishes/${encodeURIComponent(publishId)}`,
  );
  if (!result.ok) return result;
  const record = asRecord(result.value);
  const status = parseStatus(record?.status);
  if (!status) return unexpected('The publish status was unreadable.');
  return { ok: true, value: { status, findings: parseFindings(record?.validation) } };
}

export async function listPublishes(token: string): Promise<ApiResult<PublishLedger[]>> {
  const result = await request(token, '/v1/admin/policy/publishes');
  if (!result.ok) return result;
  const rows = asRecord(result.value)?.publishes;
  if (!Array.isArray(rows)) return unexpected('The history came back in an unknown shape.');
  return { ok: true, value: rows.map(parseLedger).filter((l): l is PublishLedger => l !== null) };
}

async function request(
  token: string,
  path: string,
  init?: RequestInit,
): Promise<ApiResult<unknown>> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${token}`, ...init?.headers },
    });
  } catch {
    return {
      ok: false,
      kind: 'network',
      message: 'Could not reach the API. Check your connection.',
    };
  }

  if (response.status === 401) {
    return { ok: false, kind: 'unauthorized', message: 'That token was not accepted.' };
  }

  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    // Some refusals carry no body; the status code already tells the story.
  }

  if (response.status === 409 || response.status === 400) {
    return {
      ok: false,
      kind: 'refused',
      message: String(
        asRecord(body)?.message ?? `The API refused the request (${response.status}).`,
      ),
    };
  }
  if (!response.ok) {
    return { ok: false, kind: 'unexpected', message: `The API returned ${response.status}.` };
  }
  return { ok: true, value: body };
}

function parseDraft(value: unknown): AdminDraft | null {
  const record = asRecord(value);
  if (!record || typeof record.blobName !== 'string' || typeof record.market !== 'string') {
    return null;
  }
  return {
    blobName: record.blobName,
    fileName: typeof record.fileName === 'string' ? record.fileName : record.blobName,
    market: record.market,
    topic: typeof record.topic === 'string' ? record.topic : '',
    uploadedBy: typeof record.uploadedBy === 'string' ? record.uploadedBy : '',
    uploadedAt: typeof record.uploadedAt === 'string' ? record.uploadedAt : '',
    sizeBytes: typeof record.sizeBytes === 'number' ? record.sizeBytes : 0,
    state: record.state === 'published' ? 'published' : 'staged',
    publishId: typeof record.publishId === 'string' ? record.publishId : null,
    publishedAt: typeof record.publishedAt === 'string' ? record.publishedAt : null,
  };
}

function parseStatus(value: unknown): PublishStatus | null {
  const record = asRecord(value);
  if (!record || typeof record.publishId !== 'string' || typeof record.step !== 'string') {
    return null;
  }
  const state = record.state;
  if (state !== 'running' && state !== 'succeeded' && state !== 'failed') return null;
  return {
    publishId: record.publishId,
    step: record.step,
    state,
    detail: record.detail ?? null,
    updatedAt: typeof record.updatedAt === 'string' ? record.updatedAt : '',
  };
}

function parseFindings(value: unknown): ValidationFinding[] {
  const findings = asRecord(value)?.findings;
  if (!Array.isArray(findings)) return [];
  return findings.flatMap((entry) => {
    const record = asRecord(entry);
    return record && typeof record.message === 'string'
      ? [
          {
            blobName: typeof record.blobName === 'string' ? record.blobName : '',
            kind: typeof record.kind === 'string' ? record.kind : 'finding',
            message: record.message,
          },
        ]
      : [];
  });
}

function parseLedger(value: unknown): PublishLedger | null {
  const record = asRecord(value);
  if (!record || typeof record.publishId !== 'string') return null;
  return {
    publishId: record.publishId,
    mode: typeof record.mode === 'string' ? record.mode : 'publish',
    publishedBy: typeof record.publishedBy === 'string' ? record.publishedBy : '',
    publishedAt: typeof record.publishedAt === 'string' ? record.publishedAt : '',
    blobs: Array.isArray(record.blobs) ? record.blobs.filter((b) => typeof b === 'string') : [],
    snapshotIndex: typeof record.snapshotIndex === 'string' ? record.snapshotIndex : '',
  };
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
}

function unexpected(message: string): ApiFailure {
  return { ok: false, kind: 'unexpected', message };
}
