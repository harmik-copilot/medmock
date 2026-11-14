// Load smoke test for the "list studies" endpoint. Read-only: it creates no data.
//   k6 run -e BASE_URL=http://localhost:5080 tests/load/k6-studies.js
// Credentials: QA_USER (default dr.lee) and QA_PASSWORD (default test-pass, for local use only).
// In the pipeline the password comes from a secret variable, never from this file.
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 20,
  duration: '30s',
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests under 500 ms
    http_req_failed: ['rate<0.01'],   // fewer than 1% errors
  },
};

const BASE = __ENV.BASE_URL || 'http://localhost:5080';
const USER = __ENV.QA_USER || 'dr.lee';
const PASSWORD = __ENV.QA_PASSWORD || 'test-pass';

export function setup() {
  const res = http.post(
    `${BASE}/auth/login`,
    JSON.stringify({ username: USER, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } },
  );
  check(res, { 'login succeeded': (r) => r.status === 200 });
  return { token: res.json('token') };
}

export default function (data) {
  const res = http.get(`${BASE}/studies`, {
    headers: { Authorization: `Bearer ${data.token}` },
  });
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(1);
}
