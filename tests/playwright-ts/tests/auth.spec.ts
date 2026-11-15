import { test, expect } from '@playwright/test';
import { auth } from './helpers/api';
import { qaPassword } from './helpers/env';

// Who are you? (401 territory)
test.describe('Auth', () => {
  test('valid login returns 200 and a token', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const res = await request.post('/auth/login', {
      data: { username: 'dr.lee', password: wrongPassword() },
    });

    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(typeof body.token).toBe('string');
    expect(body.token.length).toBeGreaterThan(0);
  });

  // Data-driven: one test per row, like [Theory] / [InlineData] in the C# version.
  const badCredentials: Array<[string, string]> = [
    ['dr.lee', 'wrong-password'],
    ['nobody', 'anything'],
    ['', ''],
  ];
  for (const [username, password] of badCredentials) {
    test(`bad credentials return 401 (username='${username}')`, { tag: ['@regression', '@security'] }, async ({ request }) => {
      const res = await request.post('/auth/login', { data: { username, password } });
      expect(res.status()).toBe(401);
    });
  }

  test('login failure does not reveal whether the username exists', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const wrongPassword = await request.post('/auth/login', { data: { username: 'dr.lee', password: 'nope' } });
    const unknownUser = await request.post('/auth/login', { data: { username: 'nobody', password: 'nope' } });

    expect(wrongPassword.status()).toBe(unknownUser.status());
    expect(await wrongPassword.text()).toBe(await unknownUser.text());
  });

  test('listing studies without a token returns 401', { tag: ['@smoke', '@regression', '@security'] }, async ({ request }) => {
    const res = await request.get('/studies');
    expect(res.status()).toBe(401);
  });

  test('a made-up token returns 401', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const res = await request.get('/studies', { headers: auth('not-a-real-token') });
    expect(res.status()).toBe(401);
  });
});
