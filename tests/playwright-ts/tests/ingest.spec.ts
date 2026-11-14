import { test, expect } from '@playwright/test';
import { auth, login } from './helpers/api';
import { createStudy, newStudyPayload, pushStudies } from './helpers/data';
import { environment } from './helpers/env';

// ACQUIRE: how studies get in. These tests create data, so they are skipped on read-only environments.
test.describe('Ingest', () => {
  test.skip(!environment.allowMutations, 'Environment is read-only: these tests create data');

  test('pushing studies requires authentication', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const res = await request.post('/ingest/studies', {
      data: { studies: [newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001' })] },
    });
    expect(res.status()).toBe(401);
  });

  for (const username of ['dr.lee', 'sam', 'dr.novak']) {
    test(`only providers can ingest (${username} gets 403 on both endpoints)`, { tag: ['@regression', '@security'] }, async ({ request }) => {
      const token = await login(request, username);
      const push = await request.post('/ingest/studies', {
        headers: auth(token),
        data: { studies: [newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001' })] },
      });
      const pull = await request.post('/ingest/run', { headers: auth(token) });
      expect(push.status()).toBe(403);
      expect(pull.status()).toBe(403);
    });
  }

  test('a pushed study is accepted, gets an id, and the referrer can open it', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const lee = await login(request, 'dr.lee');

    const res = await request.get(`/studies/${study.id}`, { headers: auth(lee) });

    expect(res.status()).toBe(200);
  });

  test('pushing the same study twice is reported as a duplicate (idempotent)', { tag: ['@regression'] }, async ({ request }) => {
    const payload = newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001' });

    const first = await (await pushStudies(request, 'clinic.admin', [payload])).json();
    const second = await (await pushStudies(request, 'clinic.admin', [payload])).json();

    expect(first.accepted).toBe(1);
    expect(second.accepted).toBe(0);
    expect(second.duplicates).toBe(1);
    expect(second.acceptedStudyIds).toHaveLength(0);
  });

  test('an unsupported modality is rejected with a reason and does not block the rest of the batch', { tag: ['@regression'] }, async ({ request }) => {
    const bad = newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001', modality: 'XX' });
    const good = newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001' });

    const res = await pushStudies(request, 'clinic.admin', [bad, good]);

    expect(res.status()).toBe(200);
    const summary = await res.json();
    expect(summary.accepted).toBe(1);
    expect(summary.rejected).toHaveLength(1);
    expect(summary.rejected[0].reason).toContain('modality');
  });

  test('a study dated in the future is rejected', { tag: ['@regression'] }, async ({ request }) => {
    const future = new Date(Date.now() + 3 * 86_400_000).toISOString().slice(0, 10);
    const payload = newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001', studyDate: future });

    const summary = await (await pushStudies(request, 'clinic.admin', [payload])).json();

    expect(summary.accepted).toBe(0);
    expect(summary.rejected[0].reason).toContain('future');
  });

  test('image data that is not valid base64 returns 400', { tag: ['@regression'] }, async ({ request }) => {
    const payload = { ...newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001' }), pixelsBase64: '***not base64***' };
    const res = await pushStudies(request, 'clinic.admin', [payload]);
    expect(res.status()).toBe(400);
  });

  test('an empty batch returns 400', { tag: ['@regression'] }, async ({ request }) => {
    const res = await pushStudies(request, 'clinic.admin', []);
    expect(res.status()).toBe(400);
  });

  test('a batch of more than 100 studies returns 400', { tag: ['@regression'] }, async ({ request }) => {
    const studies = Array.from({ length: 101 }, () => newStudyPayload({ referrerId: 'ref-001', patientId: 'P-1001' }));
    const res = await pushStudies(request, 'clinic.admin', studies);
    expect(res.status()).toBe(400);
  });

  test('pulling from the clinic PACS twice adds nothing the second time', { tag: ['@regression'] }, async ({ request }) => {
    const admin = await login(request, 'clinic.admin');

    const first = await request.post('/ingest/run', { headers: auth(admin) });
    const second = await request.post('/ingest/run', { headers: auth(admin) });

    expect(first.status()).toBe(200);
    expect(second.status()).toBe(200);
    expect((await second.json()).accepted).toBe(0);
  });
});
