import { randomBytes } from 'crypto';
import { APIRequestContext, expect } from '@playwright/test';
import { auth, login } from './api';

// Test data rules for shared environments:
//  * every test creates the data it needs, through the same API a clinic connector uses;
//  * everything is clearly synthetic: patient names start with QA^ and carry a run id;
//  * identifiers are unique per run, so parallel runs (and other testers) never collide;
//  * assertions check that a study IS or IS NOT visible, never "exactly N studies in total".
const runId = randomBytes(3).toString('hex').toUpperCase();
const runNumber = 100_000_000 + Math.floor(Math.random() * 899_999_999);
let counter = 0;

export interface StudyOptions {
  referrerId: string; // e.g. 'ref-001' (the referrer's user id)
  patientId: string; // e.g. 'P-1001'
  providerUser?: string; // which clinic pushes it: 'clinic.admin' (ANZ) or 'clinic.eu' (EUR)
  modality?: string;
  bodyPart?: string;
  seriesCount?: number;
  studyDate?: string; // yyyy-mm-dd, default yesterday
  pixels?: string; // text turned into base64; default synthetic
}

export interface CreatedStudy {
  id: string;
  uid: string;
  patientName: string;
  patientId: string;
  referrerId: string;
}

export function newStudyPayload(opts: StudyOptions) {
  const n = ++counter;
  const uid = `1.2.999.9.${runNumber}.${Date.now()}.${n}`;
  const patientName = `QA^${runId}${String(n).padStart(3, '0')}`;
  const yesterday = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10);
  return {
    studyInstanceUid: uid,
    patientId: opts.patientId,
    patientName,
    modality: opts.modality ?? 'DX',
    bodyPart: opts.bodyPart ?? 'CHEST',
    studyDate: opts.studyDate ?? yesterday,
    seriesCount: opts.seriesCount ?? 1,
    referrerId: opts.referrerId,
    pixelsBase64: Buffer.from(opts.pixels ?? `synthetic-pixels-${uid}`).toString('base64'),
  };
}

/** Pushes studies as a clinic would. Returns the raw response so tests can check any outcome. */
export async function pushStudies(request: APIRequestContext, providerUser: string, studies: unknown[]) {
  const token = await login(request, providerUser);
  return request.post('/ingest/studies', { headers: auth(token), data: { studies } });
}

/** Creates one synthetic study and returns its id. Fails loudly if the data was not accepted. */
export async function createStudy(request: APIRequestContext, opts: StudyOptions): Promise<CreatedStudy> {
  const payload = newStudyPayload(opts);
  const res = await pushStudies(request, opts.providerUser ?? 'clinic.admin', [payload]);
  expect(res.status(), 'Creating test data via POST /ingest/studies').toBe(200);
  const summary = await res.json();
  expect(summary.accepted, `Test data was rejected: ${JSON.stringify(summary.rejected)}`).toBe(1);
  return {
    id: summary.acceptedStudyIds[0] as string,
    uid: payload.studyInstanceUid,
    patientName: payload.patientName,
    patientId: opts.patientId,
    referrerId: opts.referrerId,
  };
}
