import { APIRequestContext, expect } from '@playwright/test';
import { qaPassword } from './env';

export interface AuditEntry {
  at: string;
  userId: string;
  action: string;
  studyId: string | null;
  allowed: boolean;
  reason: string | null;
}

// Logging in once per account per worker keeps shared environments quiet.
const tokenCache = new Map<string, string>();

/** Logs in as one of the QA accounts (or with an explicit password) and returns the token. */
export async function login(
  request: APIRequestContext,
  username: string,
  password?: string,
): Promise<string> {
  const useDefault = password === undefined;
  if (useDefault) {
    const cached = tokenCache.get(username);
    if (cached) return cached;
  }
  const res = await request.post('/auth/login', {
    data: { username, password: password ?? qaPassword() },
  });
  expect(res.status(), `Login as '${username}' should succeed`).toBe(200);
  const token = (await res.json()).token as string;
  if (useDefault) tokenCache.set(username, token);
  return token;
}

/** Header that carries the token (the "wristband"). */
export const auth = (token: string) => ({ Authorization: `Bearer ${token}` });

/** The ids (lower case) of every study this token may see. */
export async function visibleStudyIds(request: APIRequestContext, token: string): Promise<Set<string>> {
  const res = await request.get('/studies', { headers: auth(token) });
  expect(res.status(), 'Listing studies').toBe(200);
  const studies = (await res.json()) as Array<{ id: string }>;
  return new Set(studies.map((s) => s.id.toLowerCase()));
}
