import { randomUUID } from 'crypto';
import { test, expect } from '@playwright/test';
import { auth, login, visibleStudyIds } from './helpers/api';
import { createStudy } from './helpers/data';
import { environment } from './helpers/env';

// May you see this? (403 territory). The highest-risk area: the wrong person seeing a scan.
// Every test creates its own synthetic studies, so these run safely on a shared environment.
// QA accounts: dr.lee=ref-001, dr.patel=ref-002 (ANZ), dr.novak=ref-eu-1 (EUR),
//              sam=P-1001, chris=P-1002, clinic.admin (ANZ clinic), clinic.eu (EU clinic).
test.describe('Permissions', () => {
  test.skip(!environment.allowMutations, 'Environment is read-only: these tests create data');

  test("referrer sees the studies they referred, and not other referrers'", { tag: ['@smoke', '@regression', '@security'] }, async ({ request }) => {
    const mine = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const theirs = await createStudy(request, { referrerId: 'ref-002', patientId: 'P-1002' });

    const ids = await visibleStudyIds(request, await login(request, 'dr.lee'));

    expect(ids.has(mine.id)).toBe(true);
    expect(ids.has(theirs.id)).toBe(false);
  });

  test('referrer can open their own study', { tag: ['@smoke', '@regression'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const lee = await login(request, 'dr.lee');

    const res = await request.get(`/studies/${study.id}`, { headers: auth(lee) });

    expect(res.status()).toBe(200);
    expect((await res.json()).patientName).toBe(study.patientName);
  });

  test('another referrer gets 403 with no patient data in the response', { tag: ['@smoke', '@regression', '@security'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const patel = await login(request, 'dr.patel');

    const res = await request.get(`/studies/${study.id}`, { headers: auth(patel) });

    expect(res.status()).toBe(403);
    const body = await res.text();
    expect(body).not.toContain(study.patientName);
    expect(body).not.toContain('CHEST');
    expect(body.trim(), 'A 403 should not explain itself in the body').toBe('');
  });

  test("patient sees and opens their own study but not another patient's", { tag: ['@regression', '@security'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const sam = await login(request, 'sam');
    const chris = await login(request, 'chris');

    expect((await visibleStudyIds(request, sam)).has(study.id)).toBe(true);
    expect((await visibleStudyIds(request, chris)).has(study.id)).toBe(false);
    expect((await request.get(`/studies/${study.id}`, { headers: auth(sam) })).status()).toBe(200);
    expect((await request.get(`/studies/${study.id}`, { headers: auth(chris) })).status()).toBe(403);
  });

  test('unknown study id returns 404', { tag: ['@regression'] }, async ({ request }) => {
    const lee = await login(request, 'dr.lee');
    const res = await request.get(`/studies/${randomUUID()}`, { headers: auth(lee) });
    expect(res.status()).toBe(404);
  });

  test('malformed study id never causes a server error', { tag: ['@regression'] }, async ({ request }) => {
    const lee = await login(request, 'dr.lee');
    const res = await request.get('/studies/not-a-guid', { headers: auth(lee) });
    expect(res.status(), 'Expected a client error, not a 5xx').toBeLessThan(500);
  });

  test('data residency: a European referrer and an ANZ referrer never see each other\'s studies', { tag: ['@regression', '@security'] }, async ({ request }) => {
    const anz = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const eu = await createStudy(request, { referrerId: 'ref-eu-1', patientId: 'P-2001', providerUser: 'clinic.eu', modality: 'CT', bodyPart: 'HEAD' });
    const novak = await login(request, 'dr.novak');
    const lee = await login(request, 'dr.lee');

    const novakIds = await visibleStudyIds(request, novak);
    expect(novakIds.has(eu.id)).toBe(true);
    expect(novakIds.has(anz.id)).toBe(false);
    expect((await visibleStudyIds(request, lee)).has(eu.id)).toBe(false);

    expect((await request.get(`/studies/${anz.id}`, { headers: auth(novak) })).status()).toBe(403);
    expect((await request.get(`/studies/${eu.id}`, { headers: auth(lee) })).status()).toBe(403);
  });

  test('data residency beats referrer of record: a European GP who referred to an ANZ clinic is still denied', { tag: ['@regression', '@security'] }, async ({ request }) => {
    // A cross-border referral. Role rules alone would allow it (Dr Novak IS the referrer of record),
    // so only the region rule stops it. Without this test, deleting the region check goes unnoticed.
    const study = await createStudy(request, { referrerId: 'ref-eu-1', patientId: 'P-1001' }); // pushed by the ANZ clinic
    const novak = await login(request, 'dr.novak');

    expect((await visibleStudyIds(request, novak)).has(study.id)).toBe(false);
    expect((await request.get(`/studies/${study.id}`, { headers: auth(novak) })).status()).toBe(403);
  });

  test("a provider can open its own clinic's study but another clinic cannot", { tag: ['@regression', '@security'] }, async ({ request }) => {
    const study = await createStudy(request, { referrerId: 'ref-001', patientId: 'P-1001' });
    const owner = await login(request, 'clinic.admin');
    const other = await login(request, 'clinic.eu');

    expect((await request.get(`/studies/${study.id}`, { headers: auth(owner) })).status()).toBe(200);
    expect((await request.get(`/studies/${study.id}`, { headers: auth(other) })).status()).toBe(403);
  });
});
