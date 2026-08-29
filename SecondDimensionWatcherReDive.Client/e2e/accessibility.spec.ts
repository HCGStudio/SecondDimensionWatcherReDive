import AxeBuilder from "@axe-core/playwright";
import { type Page, expect, test } from "@playwright/test";

const seriousImpacts = new Set(["critical", "serious"]);

async function expectAccessible(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
    .analyze();
  const violations = results.violations.filter((violation) =>
    seriousImpacts.has(violation.impact ?? ""),
  );
  expect(
    violations,
    violations
      .map(
        (violation) =>
          `${violation.id}: ${violation.help}\n${violation.nodes
            .map((node) => `  ${node.target.join(" ")}: ${node.failureSummary}`)
            .join("\n")}`,
      )
      .join("\n\n"),
  ).toEqual([]);
}

async function expectNoHorizontalOverflow(page: Page) {
  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(
    dimensions.clientWidth + 1,
  );
}

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.addInitScript(() => {
    localStorage.setItem(
      "auth",
      JSON.stringify({
        token: "e2e-token",
        refreshToken: "e2e-refresh",
        success: true,
      }),
    );
    localStorage.setItem("i18nextLng", "en");
  });
  await page.route("**/api/images/tmdb/**", async (route) => {
    await route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ code: "tmdb_image_unavailable" }),
    });
  });
  await page.route("https://mikanani.me/**", (route) => route.abort());
});

test("chat remains operable with a keyboard and an accessible mobile drawer", async ({
  page,
}) => {
  await page.goto("/chat");
  await expect(
    page.getByRole("heading", { name: "Select or create a conversation" }),
  ).toBeVisible();

  if ((page.viewportSize()?.width ?? 1024) < 768) {
    const open = page.getByRole("button", { name: "Open conversations" });
    await open.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("dialog")).toBeVisible();
    await expectAccessible(page);
    await page.getByRole("button", { name: "New conversation" }).last().click();
  } else {
    await page.getByRole("button", { name: "New conversation" }).last().click();
  }

  await expect(page).toHaveURL(/\/chat\/[0-9a-f-]+$/);
  await expect(page.getByRole("textbox", { name: "Message" })).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await expectAccessible(page);
});

test("feed policy stays usable as a responsive card and modal workflow", async ({
  page,
}) => {
  await page.goto("/feeds");
  await expect(page.getByRole("heading", { name: "Add a feed" })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Feed URL" })).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await expectAccessible(page);

  const configure = page
    .getByRole("button", { name: /Configure automation for/ })
    .first();
  await configure.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("radio", { name: /Notify only/ })).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await expectAccessible(page);
  await page.keyboard.press("Escape");
  await expect(configure).toBeFocused();
});

test("settings and metadata review expose labelled controls without overflow", async ({
  page,
}) => {
  await page.goto("/settings?section=media");
  await expect(
    page.getByRole("heading", { name: "Media and metadata" }),
  ).toBeVisible();
  await expect(
    page.getByRole("textbox", { name: "TMDB API key", exact: true }),
  ).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await expectAccessible(page);

  await page.goto("/metadata-review");
  await expect(
    page.getByRole("heading", { name: "Metadata review center" }),
  ).toBeVisible();
  const review = page.getByRole("button", { name: "Review and edit" }).first();
  await review.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByLabel("TMDB ID")).toBeVisible();
  await expect(page.getByLabel("Season")).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await expectAccessible(page);
  await page.keyboard.press("Escape");
  await expect(review).toBeFocused();
});

test("failed poster requests show a stable placeholder and retry control", async ({
  page,
}) => {
  let posterRequestCount = 0;
  page.on("request", (request) => {
    if (request.url().includes("/api/images/tmdb/")) posterRequestCount += 1;
  });
  await page.goto("/anime/209867");
  const retry = page.getByRole("button", {
    name: /Retry image for Sousou no Frieren|Retry image for 葬送/,
  });
  await expect(retry).toBeVisible();
  const failedPoster = page.locator('[data-image-state="error"]').first();
  await expect(failedPoster).toBeVisible();
  await expect(
    failedPoster.locator("img"),
    "a failed image must be visually hidden behind the placeholder",
  ).toHaveCSS("opacity", "0");
  await retry.focus();
  await page.keyboard.press("Enter");
  await expect.poll(() => posterRequestCount).toBeGreaterThanOrEqual(3);
  await expect(failedPoster).toHaveAttribute("data-image-state", "error");
  await expectNoHorizontalOverflow(page);
  await expectAccessible(page);
});
