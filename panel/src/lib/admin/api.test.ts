import { afterEach, describe, expect, it, vi } from 'vitest';
import { getPublish, listFiles, startPublish } from './api';

function mockJson(status: number, body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() => Promise.resolve(new Response(JSON.stringify(body), { status }))),
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('admin api client', () => {
  it('parses the drafts list and drops malformed entries', async () => {
    mockJson(200, {
      v: 1,
      drafts: [
        {
          v: 1,
          blobName: 'DE/returns/a.md',
          fileName: 'a.md',
          market: 'DE',
          topic: 'returns',
          uploadedBy: 'Rasa',
          uploadedAt: '2026-08-14T12:00:00Z',
          sizeBytes: 5142,
          state: 'published',
          publishId: 'abc',
          publishedAt: '2026-08-14T12:45:00Z',
        },
        { nonsense: true },
        null,
      ],
    });

    const result = await listFiles('token');

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.value).toHaveLength(1);
      expect(result.value[0]).toMatchObject({
        blobName: 'DE/returns/a.md',
        state: 'published',
        publishId: 'abc',
      });
    }
  });

  it('turns 401 into unauthorized, not a generic error', async () => {
    mockJson(401, {});

    const result = await listFiles('wrong');

    expect(result).toMatchObject({ ok: false, kind: 'unauthorized' });
  });

  it('surfaces the server refusal message verbatim', async () => {
    mockJson(409, { message: 'Publish abc is still running (gating). One at a time.' });

    const result = await startPublish('token', ['DE/returns/a.md'], 'Rasa');

    expect(result).toMatchObject({
      ok: false,
      kind: 'refused',
      message: 'Publish abc is still running (gating). One at a time.',
    });
  });

  it('reports an unreachable API as a network failure', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('down'))),
    );

    const result = await listFiles('token');

    expect(result).toMatchObject({ ok: false, kind: 'network' });
  });

  it('parses the current-policy payload and drops malformed documents', async () => {
    mockJson(200, {
      v: 1,
      markets: ['GLOBAL', 'DE', 42],
      documents: [
        { sourcePath: 'knowledge/policy/DE/returns.md', market: 'DE', topic: 'returns', chunks: 3 },
        { broken: true },
      ],
    });

    const { getCurrent } = await import('./api');
    const result = await getCurrent('token');

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.value.markets).toEqual(['GLOBAL', 'DE']);
      expect(result.value.documents).toEqual([
        { sourcePath: 'knowledge/policy/DE/returns.md', market: 'DE', topic: 'returns', chunks: 3 },
      ]);
    }
  });

  it('parses a publish status with findings attached', async () => {
    mockJson(200, {
      v: 1,
      status: {
        publishId: 'p1',
        step: 'blocked-by-validation',
        state: 'failed',
        detail: null,
        updatedAt: '2026-08-14T13:00:00Z',
      },
      validation: {
        findings: [
          { blobName: 'GLOBAL/x/y.md', kind: 'promo-code', message: 'SUMMER25 looks like a code' },
          'garbage',
        ],
      },
    });

    const result = await getPublish('token', 'p1');

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.value.status.step).toBe('blocked-by-validation');
      expect(result.value.findings).toEqual([
        { blobName: 'GLOBAL/x/y.md', kind: 'promo-code', message: 'SUMMER25 looks like a code' },
      ]);
    }
  });
});
