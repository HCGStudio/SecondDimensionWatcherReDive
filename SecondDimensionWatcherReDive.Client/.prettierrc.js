module.exports = {
  importOrderSeparation: true,
  importOrderSortSpecifiers: true,
  importOrder: ["^@radix-ui/(.*)$", "^lucide-react$", "^[./]", ".css$"],
  plugins: [require.resolve("@trivago/prettier-plugin-sort-imports")],
};
