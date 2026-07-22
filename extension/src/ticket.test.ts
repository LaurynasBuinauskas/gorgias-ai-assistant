import { describe, expect, it } from 'vitest';
import { ticketIdFromPath } from './ticket';

describe('ticketIdFromPath', () => {
  // Real URLs from the live account: /app/views/{viewId}/{ticketId}.
  it.each([
    ['/app/views/3869628/274075680', '274075680'],
    ['/app/views/3869628/274605496', '274605496'],
    ['/app/views/3869628/275226911', '275226911'],
    ['/app/views/3869626/274351425', '274351425'],
  ])('extracts the ticket id from %s', (pathname, expected) => {
    expect(ticketIdFromPath(pathname)).toBe(expected);
  });

  it('supports direct ticket links as a fallback', () => {
    expect(ticketIdFromPath('/app/ticket/271859246')).toBe('271859246');
  });

  it('returns null for a view with no ticket selected', () => {
    expect(ticketIdFromPath('/app/views/3869628')).toBeNull();
    expect(ticketIdFromPath('/app/views/3869628/')).toBeNull();
  });

  it('returns null for non-ticket pages', () => {
    expect(ticketIdFromPath('/app/settings')).toBeNull();
    expect(ticketIdFromPath('/app')).toBeNull();
    expect(ticketIdFromPath('/')).toBeNull();
  });

  it('ignores trailing segments after the ticket id', () => {
    expect(ticketIdFromPath('/app/views/3869628/274075680/messages')).toBe('274075680');
  });

  it('does not match a non-numeric ticket segment', () => {
    expect(ticketIdFromPath('/app/views/3869628/new')).toBeNull();
  });
});
