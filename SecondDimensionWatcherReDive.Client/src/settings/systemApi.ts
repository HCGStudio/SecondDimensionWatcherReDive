import fetcher from "../auth/httpClient";
import { SystemSettings, SystemSettingsPatch } from "./systemTypes";

export const systemSettingsUrl = "/api/settings";

export const updateSystemSettings = (request: SystemSettingsPatch) =>
  fetcher<SystemSettings>(systemSettingsUrl, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
