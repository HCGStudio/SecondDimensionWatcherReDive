import brandIconSvg from "bundle-text:../favicon.svg";

import { ColorScheme, ThemeId, getThemeColors } from "./themes";

export function getBrandIconUrl(id: ThemeId, scheme: ColorScheme): string {
  const colors = getThemeColors(id, scheme);
  const highlight =
    scheme === "light" ? getThemeColors(id, "dark").brand : colors.accent;
  // Image documents cannot inherit the page's theme variables.
  const svg = brandIconSvg.replace(
    "<svg",
    `<svg style="--brand-icon-primary:${colors.brand};--brand-icon-highlight:${highlight};--brand-icon-background:${colors.tint}"`,
  );
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}
