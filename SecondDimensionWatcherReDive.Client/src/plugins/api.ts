import fetcher from "../auth/httpClient";
import {
  InstalledPlugin,
  PluginCapabilities,
  PluginPackagePreview,
} from "./types";

export const getPlugins = () => fetcher<InstalledPlugin[]>("/api/plugins");

export const previewPlugin = (packageFile: File) => {
  const body = new FormData();
  body.append("package", packageFile);
  return fetcher<PluginPackagePreview>("/api/plugins/preview", {
    method: "POST",
    body,
  });
};

export const installPlugin = (
  preview: PluginPackagePreview,
  upgrade: boolean,
) =>
  fetcher(
    `/api/plugins${upgrade ? `/${preview.manifest.id}/upgrade` : "/install"}`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        previewToken: preview.token,
        expectedSha256: preview.packageSha256,
        approvedCapabilities: preview.manifest
          .capabilities satisfies PluginCapabilities,
      }),
    },
  );

export const setPluginEnabled = (id: string, enabled: boolean) =>
  fetcher(
    `/api/plugins/${encodeURIComponent(id)}/${enabled ? "enable" : "disable"}`,
    {
      method: "POST",
    },
  );

export const uninstallPlugin = (id: string, deleteData = false) =>
  fetcher(
    `/api/plugins/${encodeURIComponent(id)}?deleteData=${String(deleteData)}`,
    { method: "DELETE" },
  );
