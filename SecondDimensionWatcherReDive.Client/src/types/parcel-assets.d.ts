declare module "*.css";

declare module "bundle-text:*" {
  const source: string;
  export default source;
}

declare module "raw-url:*" {
  const url: string;
  export default url;
}
