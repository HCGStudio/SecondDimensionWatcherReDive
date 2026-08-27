declare module "*.css";

declare module "url:@ffmpeg/core" {
  const url: string;
  export default url;
}

declare module "url:@ffmpeg/core/wasm" {
  const url: string;
  export default url;
}

declare module "bundle-text:*" {
  const source: string;
  export default source;
}
