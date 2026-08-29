import { defineConfig } from "@playwright/test";

const viewports = [
  { name: "phone-360", width: 360, height: 800 },
  { name: "phone-390", width: 390, height: 844 },
  { name: "tablet-768", width: 768, height: 1024 },
  { name: "desktop-1024", width: 1024, height: 768 },
];

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? "line" : "list",
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: "http://127.0.0.1:1234",
    locale: "en-US",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  projects: viewports.map(({ name, width, height }) => ({
    name,
    use: { viewport: { width, height } },
  })),
  webServer: [
    {
      command: "yarn mock",
      url: "http://127.0.0.1:5097/api/auth/allowregister",
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
    {
      command: "yarn start",
      url: "http://127.0.0.1:1234",
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
