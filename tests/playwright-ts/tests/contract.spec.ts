import { test, expect } from '@playwright/test';
import { auth, login } from './helpers/api';
import { createStudy } from './helpers/data';
import { environment } from './helpers/env';

// The "shape" of responses and headers. A contract change silently breaks the apps (viewer, mobile)
// that depend on it, and missing security headers are easy to overlook.
test.describe('Response contract', () => {
  test.skip(!environment.allowMutations, 'Environment is read-only: these tests create data');

  test('study list is JSON and each item has the agreed fields', { tag: ['@regression'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const token = await login(request, 'dr.lee');

    const res = await request.get('/studies', { headers: auth(token) });

    expect(res.status()).toBe(200);
    expect(res.headers()['content-type']).toContain('application/json');
    const studies = (await res.json()) as Array<Record<string, unknown>>;
    expect(studies.some((s) => s.id === study.id)).toBe(true);
    for (const item of studies) {
      for (const field of ['id', 'patientName', 'modality', 'bodyPart', 'studyDate']) {
        expect(item, `Missing field '${field}' in study list item`).toHaveProperty(field);
      }
    }
  });

  test('study list does not expose internal fields', { tag: ['@regression', '@security'] }, async ({ request }) => {
    await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const token = await login(request, 'dr.lee');

    const res = await request.get('/studies', { headers: auth(token) });

    for (const item of await res.json()) {
      for (const field of ['checksum', 'providerId', 'referrerId', 'studyInstanceUid']) {
        expect(item, `Internal field '${field}' should not be in the list`).not.toHaveProperty(field);
      }
    }
  });

  test('study detail has the agreed fields and hides internal ones', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001', modality: 'DX', bodyPart: 'CHEST' });
    const sam = await login(request, 'sam');

    const res = await request.get(`/studies/${study.id}`, { headers: auth(sam) });

    expect(res.status()).toBe(200);
    const detail = await res.json();
    for (const field of [
      'id', 'studyInstanceUid', 'patientId', 'patientName', 'modality',
      'bodyPart', 'studyDate', 'seriesCount', 'sizeBytes', 'region',
    ]) {
      expect(detail, `Missing field '${field}' in study detail`).toHaveProperty(field);
    }
    for (const field of ['checksum', 'providerId', 'referrerId']) {
      expect(detail, `Internal field '${field}' should not be in the detail`).not.toHaveProperty(field);
    }
    expect(detail.region).toBe('ANZ');
    expect(detail.modality).toBe('DX');
    expect(detail.patientName).toBe(study.patientName);
  });

  test('every response carries a correlation id', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const res = await request.get('/studies'); // 401, still must carry one
    expect(res.headers()['x-correlation-id']).toMatch(/^[A-Za-z0-9_-]{1,64}$/);
  });

  test('a supplied correlation id is echoed back', { tag: ['@regression'] }, async ({ request }) => {
    const res = await request.get('/health/live', { headers: { 'X-Correlation-ID': 'qa-trace-12345' } });
    expect(res.headers()['x-correlation-id']).toBe('qa-trace-12345');
  });

  test('an unsafe correlation id is replaced, not echoed', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const res = await request.get('/health/live', { headers: { 'X-Correlation-ID': 'bad id with spaces & <tags>' } });
    const returned = res.headers()['x-correlation-id'];
    expect(returned).not.toContain('<');
    expect(returned).toMatch(/^[A-Za-z0-9_-]{1,64}$/);
  });

  test('responses are sent with X-Content-Type-Options: nosniff', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const res = await request.get('/health/live');
    expect(res.headers()['x-content-type-options']).toBe('nosniff');
  });
});
