module.exports = {
  importOrderSeparation: true,
  importOrderSortSpecifiers: true,
  importOrder: ["^@elastic/eui/es/components/icon/(.*)$", "^[./]", ".css$"],
  plugins: [require.resolve("@trivago/prettier-plugin-sort-imports")],
};
