import * as fs from 'fs';
import * as path from 'path';

export interface EnvironmentConfig {
  name: string;
  description: string;
  baseUrl: string;
  /** false = read-only environment: tests that create data are skipped */
  allowMutations: boolean;
  /** what GET /version should report, to prove we are testing the environment we think we are */
  expectedAspNetEnvironment: string;
}

export function loadEnvironment(): EnvironmentConfig {
  const name = process.env.TARGET_ENV ?? 'local';
  if (!/^[a-z]+$/.test(name)) {
    throw new Error(`TARGET_ENV '${name}' is not valid. Use local, test or staging.`);
  }
  const file = path.resolve(__dirname, '../../../environments', `${name}.json`);
  if (!fs.existsSync(file)) {
    throw new Error(`Unknown TARGET_ENV '${name}': expected a file at ${file}`);
  }
  const config = JSON.parse(fs.readFileSync(file, 'utf8')) as EnvironmentConfig;
  return { ...config, baseUrl: process.env.MED_API_URL ?? config.baseUrl };
}

export const environment = loadEnvironment();

/** The shared password of the pre-provisioned QA accounts. Secrets come from the environment, never from the repo. */
export function qaPassword(): string {
  const fromEnv = process.env.MED_QA_PASSWORD;
  if (fromEnv) return fromEnv;
  if (environment.name === 'local') return 'test-pass'; // local development only
  throw new Error(`MED_QA_PASSWORD must be set when TARGET_ENV is '${environment.name}'.`);
}
