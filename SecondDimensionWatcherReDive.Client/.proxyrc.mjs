import { createProxyMiddleware } from "http-proxy-middleware";

const url = process.env["BACKEND_URL"] ?? "http://localhost:5097";

export default function (app) {
  app.use(
    createProxyMiddleware({
      target: url,
      pathFilter: "/api",
    }),
  );
}
