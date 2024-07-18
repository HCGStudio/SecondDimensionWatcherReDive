import { createProxyMiddleware } from "http-proxy-middleware";

const url = `${process.env["services__backend__https__0"] ?? process.env["services__backend__http__0"]}/api`;

export default function (app) {
  app.use(
    "/api",
    createProxyMiddleware({
      target: url,
    }),
  );
}
