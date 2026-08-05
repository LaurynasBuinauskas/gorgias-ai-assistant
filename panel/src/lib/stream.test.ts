import { afterEach, describe, expect, it, vi } from 'vitest';
import { type StreamEvent, streamDraft } from './stream';

/** Builds an SSE response body from raw frames, delivered in one chunk unless split. */
function sseResponse(frames: readonly string[], status = 200): Response {
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      const encoder = new TextEncoder();
      for (const frame of frames) {
        controller.enqueue(encoder.encode(frame));
      }
      controller.close();
    },
  });
  return new Response(body, { status });
}

function mockFetch(response: Response | (() => Promise<Response>)) {
  const impl = typeof response === 'function' ? response : () => Promise.resolve(response);
  vi.stubGlobal('fetch', vi.fn(impl));
}

async function collect(generator: AsyncGenerator<StreamEvent>): Promise<StreamEvent[]> {
  const events: StreamEvent[] = [];
  for await (const event of generator) {
    events.push(event);
  }
  return events;
}

afterEach(() => vi.unstubAllGlobals());

describe('streamDraft', () => {
  it('parses events in order, including frames split across chunks', async () => {
    mockFetch(
      sseResponse([
        'event: ticket\ndata: {"customerName":"Jane","subject":"Order","language":"de","messageCount":3}\n\n',
        'event: delta\ndata: {"text":"Hal',
        'lo"}\n\nevent: delta\ndata: {"text":" Jane"}\n\n',
        'event: done\ndata: {"draftId":"d1"}\n\n',
      ]),
    );

    const events = await collect(streamDraft('token', '42', { turns: [] }));

    expect(events.map((e) => e.kind)).toEqual(['ticket', 'delta', 'delta', 'done']);
    expect(events[0]).toMatchObject({ ticket: { customerName: 'Jane', messageCount: 3 } });
    expect(
      events
        .filter((e) => e.kind === 'delta')
        .map((e) => (e.kind === 'delta' ? e.text : ''))
        .join(''),
    ).toBe('Hallo Jane');
  });

  it('parses progress stages and drops unknown or malformed ones', async () => {
    mockFetch(
      sseResponse([
        'event: progress\ndata: {"stage":"searched","market":"DE","signal":"recipientaddress","policy":4,"templates":1,"pastTickets":3,"internalGuides":2}\n\n',
        // Malformed counts become zero rather than NaN reaching the UI.
        'event: progress\ndata: {"stage":"searched","market":"UK","signal":"shopdomain","policy":"lots","pastTickets":-1}\n\n',
        'event: progress\ndata: {"stage":"coverage","decision":"passed"}\n\n',
        'event: progress\ndata: {"stage":"drafting"}\n\n',
        // A stage or decision this panel does not know is dropped, like an unknown event name.
        'event: progress\ndata: {"stage":"mystery"}\n\n',
        'event: progress\ndata: {"stage":"coverage","decision":"perhaps"}\n\n',
        'event: done\ndata: {}\n\n',
      ]),
    );

    const events = await collect(streamDraft('token', '42', { turns: [] }));

    expect(events).toEqual([
      {
        kind: 'progress',
        progress: {
          stage: 'searched',
          market: 'DE',
          signal: 'recipientaddress',
          policy: 4,
          templates: 1,
          pastTickets: 3,
          internalGuides: 2,
        },
      },
      {
        kind: 'progress',
        progress: {
          stage: 'searched',
          market: 'UK',
          signal: 'shopdomain',
          policy: 0,
          templates: 0,
          pastTickets: 0,
          internalGuides: 0,
        },
      },
      { kind: 'progress', progress: { stage: 'coverage', decision: 'passed' } },
      { kind: 'progress', progress: { stage: 'drafting' } },
      { kind: 'done', citations: [] },
    ]);
  });

  it('reports insufficient_data as its own event, not an error', async () => {
    mockFetch(sseResponse(['event: insufficient\ndata: {"message":"No customer message."}\n\n']));

    const events = await collect(streamDraft('token', '42', { turns: [] }));

    expect(events).toEqual([{ kind: 'insufficient', message: 'No customer message.' }]);
  });

  it.each([
    [401, 'Unauthorized — check your access token.'],
    [404, 'Ticket 42 was not found.'],
    [429, 'Too many drafts in a row — wait a moment and try again.'],
  ])('turns HTTP %i into a readable error', async (status, message) => {
    mockFetch(new Response(null, { status }));

    const events = await collect(streamDraft('token', '42', { turns: [] }));

    expect(events).toEqual([{ kind: 'error', message }]);
  });

  it('reports an unreachable API', async () => {
    mockFetch(() => Promise.reject(new TypeError('network down')));

    const events = await collect(streamDraft('token', '42', { turns: [] }));

    expect(events).toEqual([
      { kind: 'error', message: 'Could not reach the assistant. Check your connection.' },
    ]);
  });

  it('stays silent when the caller aborts before the request starts', async () => {
    const controller = new AbortController();
    controller.abort();
    mockFetch(() => Promise.reject(new DOMException('Aborted', 'AbortError')));

    const events = await collect(streamDraft('token', '42', { turns: [] }, controller.signal));

    // An abort is the caller's own doing — surfacing it as an error would show the agent
    // a failure for something they triggered.
    expect(events).toEqual([]);
  });

  it('stays silent when the caller aborts mid-stream', async () => {
    const controller = new AbortController();
    const body = new ReadableStream<Uint8Array>({
      start(streamController) {
        streamController.enqueue(new TextEncoder().encode('event: delta\ndata: {"text":"Hi"}\n\n'));
        // Never closes: the abort below is what ends the read.
      },
    });
    mockFetch(new Response(body, { status: 200 }));

    const events: StreamEvent[] = [];
    for await (const event of streamDraft('token', '42', { turns: [] }, controller.signal)) {
      events.push(event);
      controller.abort();
      break;
    }

    expect(events).toEqual([{ kind: 'delta', text: 'Hi' }]);
  });

  it('sends the bearer token, the conversation and the instruction', async () => {
    mockFetch(sseResponse(['event: done\ndata: {}\n\n']));

    await collect(
      streamDraft(
        'secret-token',
        '271859246',
        { turns: [{ role: 'assistant', text: 'Hallo' }], instruction: 'translate to English' },
        undefined,
      ),
    );

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toContain('/v1/tickets/271859246/drafts/stream');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer secret-token');
    expect(JSON.parse(init.body as string)).toEqual({
      v: 1,
      turns: [{ role: 'assistant', text: 'Hallo' }],
      instruction: 'translate to English',
    });
  });

  it('replays only the wire fields of a turn, not the panel-only timeline', async () => {
    mockFetch(sseResponse(['event: done\ndata: {}\n\n']));

    await collect(
      streamDraft('t', '1', {
        turns: [{ role: 'assistant', text: 'Hallo', progress: [{ stage: 'drafting' }] }],
      }),
    );

    const [, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string).turns).toEqual([{ role: 'assistant', text: 'Hallo' }]);
  });

  it('classifies citations by where they came from', async () => {
    // The backend has always sent these; the panel discarded them. What an agent needs from
    // this list is which claims came from published policy and which from someone else's
    // ticket, so the kind is derived here rather than trusted from the wire.
    mockFetch(
      sseResponse([
        'event: done\ndata: {"draftId":"d1","citations":[' +
          '{"label":"P1","sourcePath":"knowledge/policy/DE/shipping-and-returns.md","market":"DE"},' +
          '{"label":"T1","sourcePath":"knowledge/templates/order-status/late.md","market":"GLOBAL"},' +
          '{"label":"X1","sourcePath":"gorgias/ticket/242012080","market":"GLOBAL"}]}\n\n',
      ]),
    );

    const events = await collect(streamDraft('t', '1', { turns: [] }, undefined));
    const done = events.find((event) => event.kind === 'done');

    expect(done).toEqual({
      kind: 'done',
      citations: [
        {
          label: 'P1',
          sourcePath: 'knowledge/policy/DE/shipping-and-returns.md',
          market: 'DE',
          kind: 'policy',
        },
        {
          label: 'T1',
          sourcePath: 'knowledge/templates/order-status/late.md',
          market: 'GLOBAL',
          kind: 'template',
        },
        { label: 'X1', sourcePath: 'gorgias/ticket/242012080', market: 'GLOBAL', kind: 'ticket' },
      ],
    });
  });

  it('survives a done event with missing or malformed citations', async () => {
    // A type assertion is not validation, and this is the one field an older or a broken
    // backend is most likely to omit.
    mockFetch(
      sseResponse([
        'event: done\ndata: {"draftId":"d1"}\n\n',
        'event: done\ndata: {"citations":[{"label":"P1"},"nonsense",null,{"sourcePath":"x"}]}\n\n',
      ]),
    );

    const events = await collect(streamDraft('t', '1', { turns: [] }, undefined));

    expect(events).toEqual([
      { kind: 'done', citations: [] },
      { kind: 'done', citations: [] },
    ]);
  });
});
