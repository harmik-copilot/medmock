import { test, expect } from '@playwright/test';
import { environment } from './helpers/env';

// Is it up, and is it the environment we think it is?
test.describe('Health and environment', () => {
  test('live endpoint returns 200', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const res = await request.get('/health/live');
    expect(res.status()).toBe(200);
  });

  test('ready endpoint returns 200', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const res = await request.get('/health/ready');
    expect(res.status()).toBe(200);
  });

  test('version reports the environment these tests expect', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const res = await request.get('/version');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(
      body.environment,
      `Tests are pointed at '${environment.name}' but the API says it is '${body.environment}'. Wrong URL?`,
    ).toBe(environment.expectedAspNetEnvironment);
    expect(typeof body.version).toBe('string');
    expect(body.version.length).toBeGreaterThan(0);
  });
});
