// Parcel classic scripts cannot import modules. Apply the saved color scheme
// before paint using browser colors, without copying the theme palettes here.
// ThemeProvider applies the selected palette before the first React paint.
(() => {
  let mode: "light" | "dark" | undefined;
  try {
    const saved = JSON.parse(
      window.localStorage.getItem("sdw.theme") ?? "null",
    );
    if (saved?.mode === "light" || saved?.mode === "dark") mode = saved.mode;
  } catch {
    // Restricted or invalid storage falls back to the system preference.
  }
  const scheme =
    mode ??
    (typeof window.matchMedia === "function" &&
    window.matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light");
  const root = document.documentElement;
  root.dataset.colorScheme = scheme;
  root.style.colorScheme = scheme;
  root.style.backgroundColor = "Canvas";
  root.style.color = "CanvasText";
})();
