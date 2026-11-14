import { defineConfig } from '@playwright/test';
import { loadEnvironment } from './tests/helpers/env';

// Which environment are we testing?  TARGET_ENV=local|test|staging   (default: local)
// The settings for each live in tests/environments/<name>.json, shared with the C# suite.
// MED_API_URL overrides the address, MED_QA_PASSWORD supplies the QA accounts' password (never commit it).
const env = loadEnvironment();

// Only locally do we start the API ourselves. Test and staging are deployed by the pipeline.
const startLocalApi = env.name === 'local' && !process.env.MED_API_URL;

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
    ['junit', { outputFile: 'reports/junit.xml' }], // for the pipeline's test results tab
  ],

  use: {
    baseURL: env.baseUrl,
    extraHTTPHeaders: { Accept: 'application/json' },
  },

  webServer: startLocalApi
    ? {
        command: 'dotnet run --project src/Med.Api --urls http://localhost:5080 --no-launch-profile',
        cwd: '../..',
        env: { ASPNETCORE_ENVIRONMENT: 'Development' },
        url: 'http://localhost:5080/health/live',
        reuseExistingServer: true,
        timeout: 180_000, // the first run has to build the API
      }
    : undefined,
});
