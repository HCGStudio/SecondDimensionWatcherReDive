import { Page, expect, test } from "@playwright/test";

const auth = {
  success: true,
  token: "playwright-token",
  refreshToken: "playwright-refresh-token",
};

async function selectEnglish(page: Page): Promise<void> {
  await page.addInitScript(() => localStorage.setItem("i18n.lng", "en"));
}

async function authenticate(page: Page): Promise<void> {
  await page.addInitScript((value) => {
    localStorage.setItem("i18n.lng", "en");
    localStorage.setItem("auth", JSON.stringify(value));
  }, auth);
}

const isFfmpegRequest = (url: string): boolean =>
  /(?:ffmpeg|transcoder)(?:[./-]|$)/i.test(new URL(url).pathname);

test.describe("production boundary journeys", () => {
  test("serves the optimized production bundle through SPA fallback", async ({
    page,
    request,
  }) => {
    const routeResponse = await request.get("/metadata-review", {
      headers: { Accept: "text/html" },
    });
    expect(routeResponse.ok()).toBeTruthy();
    expect(routeResponse.headers()["x-sdw-frontend-artifact"]).toBe(
      "production",
    );
    const html = await routeResponse.text();
    expect(html).not.toContain("/@parcel/");
    const scriptMatch = html.match(
      /<script[^>]+\bsrc=(?:"([^"]+)"|'([^']+)'|([^\s>]+))/i,
    );
    const optimizedScript = scriptMatch?.slice(1).find(Boolean);
    expect(optimizedScript).toMatch(/^\/[^?]+\.[a-f0-9]{8,}\.js$/i);

    const assetResponse = await request.get(optimizedScript!);
    expect(assetResponse.ok()).toBeTruthy();
    expect(assetResponse.headers()["content-type"]).toContain("javascript");
    expect(assetResponse.headers()["cache-control"]).toContain("immutable");

    await authenticate(page);
    const requestedAssets: string[] = [];
    page.on("request", (requested) => requestedAssets.push(requested.url()));
    await page.goto("/metadata-review");
    await expect(
      page.getByRole("heading", { name: "Metadata review center" }),
    ).toBeVisible();
    expect(
      requestedAssets.some((url) =>
        /\/MetadataReviewPage\.[a-f0-9]{8,}\.js(?:\?.*)?$/i.test(url),
      ),
    ).toBeTruthy();
    expect(requestedAssets.some(isFfmpegRequest)).toBeFalsy();
  });

  test("recovers from a missing lazy chunk by reloading without prefetching FFmpeg", async ({
    page,
  }) => {
    await authenticate(page);
    const requestedAssets: string[] = [];
    let failedChunkRequests = 0;
    page.on("request", (requested) => requestedAssets.push(requested.url()));
    await page.route(
      /\/SettingsPage\.[a-f0-9]{8,}\.js(?:\?.*)?$/i,
      async (route) => {
        if (failedChunkRequests === 0) {
          failedChunkRequests += 1;
          await route.fulfill({
            status: 404,
            contentType: "text/plain",
            body: "simulated stale chunk",
          });
          return;
        }
        await route.continue();
      },
    );

    await page.goto("/settings");
    await expect(
      page.getByRole("heading", {
        name: "Something went wrong",
      }),
    ).toBeVisible();
    expect(failedChunkRequests).toBe(1);
    expect(requestedAssets.some(isFfmpegRequest)).toBeFalsy();

    await page.getByRole("button", { name: "Retry" }).click();
    await expect(
      page.getByRole("heading", { name: "System settings" }),
    ).toBeVisible();
    expect(
      requestedAssets.filter((url) =>
        /\/SettingsPage\.[a-f0-9]{8,}\.js(?:\?.*)?$/i.test(url),
      ),
    ).toHaveLength(2);
    expect(requestedAssets.some(isFfmpegRequest)).toBeFalsy();
  });

  test("registers, signs out locally, and signs back in", async ({ page }) => {
    await selectEnglish(page);
    await page.goto("/login");

    const setupHeading = page.getByRole("heading", { name: "Set a password" });
    const loginHeading = page.getByRole("heading", { name: "Welcome back" });
    await expect(setupHeading.or(loginHeading)).toBeVisible();
    if (await setupHeading.isVisible()) {
      const registrationPasswords = page.locator('input[type="password"]');
      await registrationPasswords.nth(0).fill("correct horse battery staple");
      await registrationPasswords.nth(1).fill("correct horse battery staple");
      await page.getByRole("button", { name: "Register" }).click();
    } else {
      await page
        .locator('input[type="password"]')
        .fill("correct horse battery staple");
      await page
        .getByRole("main")
        .getByRole("button", { name: "Sign in" })
        .click();
    }
    await expect(page.getByRole("heading", { name: "Anime" })).toBeVisible();

    await page.evaluate(() => localStorage.removeItem("auth"));
    await page.goto("/login");
    await expect(
      page.getByRole("heading", { name: "Welcome back" }),
    ).toBeVisible();
    await page
      .locator('input[type="password"]')
      .fill("correct horse battery staple");
    await page
      .getByRole("main")
      .getByRole("button", { name: "Sign in" })
      .click();
    await expect(page.getByRole("heading", { name: "Anime" })).toBeVisible();
  });

  test("pauses downloads, browses VFS files, and updates watched state", async ({
    page,
  }) => {
    await authenticate(page);

    const animationsResponse = await page.request.get(
      "http://127.0.0.1:5097/api/animationinfo?skip=0&take=100",
      { headers: { Authorization: `Bearer ${auth.token}` } },
    );
    expect(animationsResponse.ok()).toBeTruthy();
    const animations = (await animationsResponse.json()) as {
      data: { id: string; title: string; isDownloadTracked: boolean }[];
    };
    const candidate = animations.data.find((item) => !item.isDownloadTracked);
    if (!candidate) throw new Error("Mock server has no candidate download");
    const startResponse = await page.request.post(
      `http://127.0.0.1:5097/api/animationinfo/download/${candidate.id}`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    );
    expect(startResponse.ok()).toBeTruthy();

    await page.goto("/downloading");
    const downloadRow = page
      .getByRole("heading", { name: candidate.title, exact: true })
      .locator("xpath=../../..");
    await downloadRow.getByTitle("Pause").click();
    await expect(downloadRow.getByTitle("Resume")).toBeVisible();
    await downloadRow.getByTitle("Resume").click();
    await expect(downloadRow.getByTitle("Pause")).toBeVisible();
    await downloadRow.getByTitle("Pause").click();
    await expect(downloadRow.getByTitle("Resume")).toBeVisible();

    await page.goto("/files");
    await expect(page.getByRole("heading", { name: "Files" })).toBeVisible();
    await page.getByRole("button", { name: "葬送のフリーレン" }).click();
    await page.getByRole("button", { name: "SubsPlease" }).click();
    await expect(page.getByText("葬送のフリーレン S01E01.mkv")).toBeVisible();
    await expect(page.getByTitle("Download").first()).toBeEnabled();

    await page.goto("/downloaded");
    await page.getByTitle("Browse files").first().click();
    await page.getByRole("button", { name: "Season 1" }).click();
    const markWatched = page
      .getByRole("button", { name: "Mark watched" })
      .first();
    const markUnwatched = page
      .getByRole("button", { name: "Mark unwatched" })
      .first();
    await expect(markWatched.or(markUnwatched)).toBeVisible();
    const startsWatched = await markUnwatched.isVisible();
    const toggle = startsWatched ? markUnwatched : markWatched;
    const expectedToggle = startsWatched ? markWatched : markUnwatched;
    const watchedResponse = page.waitForResponse(
      (response) =>
        response.url().includes("/api/playback/watched") &&
        response.request().method() === "PUT",
    );
    await toggle.click();
    await expect((await watchedResponse).ok()).toBeTruthy();
    await expect(expectedToggle).toBeVisible();
  });

  test("loads subscription, metadata-review, and incident recovery surfaces", async ({
    page,
  }) => {
    await authenticate(page);

    await page.goto("/feeds");
    await page
      .getByRole("button", { name: "Configure automation for 葬送的芙莉莲" })
      .click();
    await expect(
      page.getByRole("heading", { name: "Automation policy" }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Run simulation" }).click();
    await expect(page.getByText(/historical releases match/)).toBeVisible();

    await page.goto("/metadata-review");
    await expect(
      page.getByRole("heading", { name: "Metadata review center" }),
    ).toBeVisible();
    await page.getByRole("tab", { name: /^Failed/ }).click();
    await expect(
      page.getByRole("heading", { name: "Failed identifications" }),
    ).toBeVisible();

    await page.goto("/incidents");
    await expect(
      page.getByRole("heading", { name: "Issue inbox" }),
    ).toBeVisible();
    await expect(
      page.getByText("Download volume is almost full"),
    ).toBeVisible();
    await expect(page.getByRole("button", { name: "Retry all" })).toBeEnabled();
  });

  test("creates a conversation and consumes the streamed chat response", async ({
    page,
  }) => {
    await authenticate(page);
    await page.goto("/chat");

    await page
      .getByRole("button", { name: "New conversation" })
      .first()
      .click();
    await expect(page).toHaveURL(/\/chat\/[0-9a-f-]+$/);
    const input = page.getByPlaceholder(
      "Type a message... (Shift+Enter for new line)",
    );
    await input.fill("Which anime are available?");
    await input.press("Enter");

    await expect(page.getByText("Which anime are available?")).toBeVisible();
    await expect(page.getByText("查询结果")).toBeVisible({ timeout: 15_000 });
    await expect(
      page.getByText("葬送的芙莉莲", { exact: true }).last(),
    ).toBeVisible();
  });
});
