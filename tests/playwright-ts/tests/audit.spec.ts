import { test, expect } from '@playwright/test';
import { AuditEntry, auth, login } from './helpers/api';
import { createStudy } from './helpers/data';
import { environment } from './helpers/env';

// Can we investigate afterwards? Every access attempt should leave a trace.
test.describe('Audit trail', () => {
  const sameId = (a: string | null, b: string) => a?.toLowerCase() === b.toLowerCase();

  async function auditAs(request: Parameters<typeof login>[0], username: string): Promise<AuditEntry[]> {
    const token = await login(request, username);
    const res = await request.get('/audit', { headers: auth(token) });
    expect(res.status()).toBe(200);
    return res.json();
  }

  test.describe('with test data', () => {
    test.skip(!environment.allowMutations, 'Environment is read-only: these tests create data');

    test('denied access is recorded for the owning provider', { tag: ['@regression', '@security'] }, async ({ request }) => {
      const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
      await request.get(`/studies/${study.id}`, { headers: auth(await login(request, 'dr.patel')) }); // the denied attempt

      const entries = await auditAs(request, 'clinic.admin');

      const found = entries.some(
        (e) => e.userId === 'ref-002' && e.action === 'OPEN_STUDY' && sameId(e.studyId, study.id) && e.allowed === false,
      );
      expect(found, 'Expected a denied OPEN_STUDY entry for ref-002').toBe(true);
    });

    test('allowed access is recorded too', { tag: ['@regression'] }, async ({ request }) => {
      const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
      await request.get(`/studies/${study.id}`, { headers: auth(await login(request, 'dr.lee')) });

      const entries = await auditAs(request, 'clinic.admin');

      const found = entries.some(
        (e) => e.userId === 'ref-001' && e.action === 'OPEN_STUDY' && sameId(e.studyId, study.id) && e.allowed === true,
      );
      expect(found, 'Expected an allowed OPEN_STUDY entry for ref-001').toBe(true);
    });

    test("another provider's audit view does not include this study", { tag: ['@regression', '@security'] }, async ({ request }) => {
      const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
      await request.get(`/studies/${study.id}`, { headers: auth(await login(request, 'dr.patel')) });

      const entries = await auditAs(request, 'clinic.eu');

      expect(
        entries.some((e) => sameId(e.studyId, study.id)),
        'The EU clinic must not see audit entries about an ANZ clinic study',
      ).toBe(false);
    });
  });

  for (const username of ['dr.lee', 'sam']) {
    test(`only providers can read the audit trail (${username} gets 403)`, { tag: ['@regression', '@security'] }, async ({ request }) => {
      const token = await login(request, username);
      const res = await request.get('/audit', { headers: auth(token) });
      expect(res.status()).toBe(403);
    });
  }

  test('audit trail without a token returns 401', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const res = await request.get('/audit');
    expect(res.status()).toBe(401);
  });
});
