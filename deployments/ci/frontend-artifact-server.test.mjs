import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { createFrontendArtifactServer } from "./frontend-artifact-server.mjs";

function listen(server) {
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      server.off("error", reject);
      resolve(server.address().port);
    });
  });
}

function close(server) {
  return new Promise((resolve, reject) => {
    server.close((error) => (error ? reject(error) : resolve()));
  });
}

test("serves optimized assets, falls back for SPA routes, and proxies the mock API", async (context) => {
  const fixtureRoot = await mkdtemp(join(tmpdir(), "sdw-frontend-artifact-"));
  await Promise.all([
    writeFile(
      join(fixtureRoot, "index.html"),
      '<!doctype html><script type="module" src="/app.a1b2c3d4.js"></script>',
    ),
    writeFile(
      join(fixtureRoot, "app.a1b2c3d4.js"),
      "globalThis.production = true;",
    ),
  ]);

  const apiServer = createServer((request, response) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => {
      response.writeHead(201, {
        "Content-Type": "application/json",
        "X-Mock": "yes",
      });
      response.end(
        JSON.stringify({
          body: Buffer.concat(chunks).toString(),
          method: request.method,
          url: request.url,
        }),
      );
    });
  });
  const apiPort = await listen(apiServer);
  const frontendServer = createFrontendArtifactServer({
    rootDirectory: fixtureRoot,
    apiOrigin: `http://127.0.0.1:${apiPort}`,
  });
  const frontendPort = await listen(frontendServer);
  context.after(async () => {
    frontendServer.closeAllConnections();
    apiServer.closeAllConnections();
    await Promise.all([close(frontendServer), close(apiServer)]);
    await rm(fixtureRoot, { recursive: true, force: true });
  });
  const origin = `http://127.0.0.1:${frontendPort}`;

  const routeResponse = await fetch(`${origin}/settings/provider`, {
    headers: { Accept: "text/html" },
  });
  assert.equal(routeResponse.status, 200);
  assert.equal(
    routeResponse.headers.get("x-sdw-frontend-artifact"),
    "production",
  );
  assert.match(await routeResponse.text(), /app\.a1b2c3d4\.js/);

  const assetResponse = await fetch(`${origin}/app.a1b2c3d4.js`);
  assert.equal(assetResponse.status, 200);
  assert.equal(
    assetResponse.headers.get("content-type"),
    "text/javascript; charset=utf-8",
  );
  assert.equal(
    assetResponse.headers.get("cache-control"),
    "public, max-age=31536000, immutable",
  );
  assert.equal(await assetResponse.text(), "globalThis.production = true;");

  const missingAsset = await fetch(`${origin}/missing.12345678.js`);
  assert.equal(missingAsset.status, 404);

  const apiResponse = await fetch(`${origin}/api/check?source=production`, {
    body: "payload",
    headers: { "Content-Type": "text/plain" },
    method: "POST",
  });
  assert.equal(apiResponse.status, 201);
  assert.equal(apiResponse.headers.get("x-mock"), "yes");
  assert.deepEqual(await apiResponse.json(), {
    body: "payload",
    method: "POST",
    url: "/api/check?source=production",
  });
});

test("Playwright harness consumes the uploaded production artifact without Parcel", async () => {
  const config = await readFile(
    new URL(
      "../../SecondDimensionWatcherReDive.Client/playwright.config.ts",
      import.meta.url,
    ),
    "utf8",
  );
  assert.match(config, /baseURL: "http:\/\/127\.0\.0\.1:4173"/);
  assert.match(config, /command: "yarn serve:e2e"/);
  assert.doesNotMatch(config, /yarn start|\bparcel\b/i);

  const packageJson = JSON.parse(
    await readFile(
      new URL(
        "../../SecondDimensionWatcherReDive.Client/package.json",
        import.meta.url,
      ),
      "utf8",
    ),
  );
  assert.equal(
    packageJson.scripts["serve:e2e"],
    "node ../deployments/ci/frontend-artifact-server.mjs dist",
  );

  const workflow = await readFile(
    new URL("../../.github/workflows/verify.yml", import.meta.url),
    "utf8",
  );
  const e2eJob = workflow.match(
    /  frontend_e2e:\n([\s\S]*?)\n  fuse_mount_smoke:/,
  )?.[1];
  assert.ok(e2eJob, "frontend_e2e job is missing");
  assert.match(e2eJob, /uses: actions\/download-artifact@v8/);
  assert.match(e2eJob, /name: verify-frontend-dist/);
  assert.match(e2eJob, /path: SecondDimensionWatcherReDive\.Client\/dist\//);
});
