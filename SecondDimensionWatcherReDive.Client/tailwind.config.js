/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{ts,tsx,html}"],
  theme: {
    extend: {
      colors: {
        canvas: "#f5f4ed",
        surface: "#faf9f5",
        brand: "#c96442",
        accent: "#d97757",
        foreground: "#141413",
        muted: "#5e5d59",
        subtle: "#87867f",
        border: "#e8e6dc",
        "border-light": "#f0eee6",
        "dark-surface": "#30302e",
        "dark-deep": "#141413",
        focus: "#3898ec",
        error: "#b53333",
        success: "#198754",
        warning: "#e6a23c",
        "warm-silver": "#b0aea5",
        "charcoal-warm": "#4d4c48",
        "dark-warm": "#3d3d3a",
        "ring-warm": "#d1cfc5",
        "ring-deep": "#c2c0b6",
      },
      fontFamily: {
        serif: [
          "'Source Serif 4'",
          "'Noto Serif SC'",
          "Georgia",
          "Cambria",
          "'Times New Roman'",
          "serif",
        ],
        sans: [
          "Inter",
          "'Noto Sans SC'",
          "system-ui",
          "-apple-system",
          "BlinkMacSystemFont",
          "'Segoe UI'",
          "sans-serif",
        ],
        mono: [
          "'JetBrains Mono'",
          "ui-monospace",
          "SFMono-Regular",
          "'SF Mono'",
          "monospace",
        ],
      },
      lineHeight: {
        "heading-tight": "1.10",
        heading: "1.20",
        "heading-relaxed": "1.30",
        body: "1.60",
      },
      borderRadius: {
        sm: "4px",
        DEFAULT: "6px",
        md: "8px",
        lg: "12px",
        xl: "16px",
        "2xl": "24px",
        "3xl": "32px",
      },
      boxShadow: {
        ring: "0px 0px 0px 1px #d1cfc5",
        "ring-brand": "0px 0px 0px 1px #c96442",
        whisper: "rgba(0,0,0,0.05) 0px 4px 24px",
        inset: "inset 0px 0px 0px 1px rgba(0,0,0,0.15)",
      },
      keyframes: {
        "slide-in-right": {
          from: { transform: "translateX(100%)" },
          to: { transform: "translateX(0)" },
        },
        "slide-out-right": {
          from: { transform: "translateX(0)" },
          to: { transform: "translateX(100%)" },
        },
        "toast-in": {
          from: { transform: "translateX(100%)", opacity: "0" },
          to: { transform: "translateX(0)", opacity: "1" },
        },
        "toast-out": {
          from: { transform: "translateX(0)", opacity: "1" },
          to: { transform: "translateX(100%)", opacity: "0" },
        },
      },
      animation: {
        "slide-in-right": "slide-in-right 200ms ease-out",
        "slide-out-right": "slide-out-right 200ms ease-in",
        "toast-in": "toast-in 200ms ease-out",
        "toast-out": "toast-out 200ms ease-in",
      },
      typography: {
        DEFAULT: {
          css: {
            "--tw-prose-links": "#c96442",
            "--tw-prose-code": "#141413",
            "code::before": { content: '""' },
            "code::after": { content: '""' },
          },
        },
      },
    },
  },
  plugins: [require("@tailwindcss/typography")],
};
