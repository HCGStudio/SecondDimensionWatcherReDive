export interface SdwPluginHost {
  request(capability: "network.request", payload: {
    method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
    url: string;
    body?: string;
    contentType?: string;
  }): { status: number; contentType?: string; body: string };
  request(capability: "file.read", payload: { path: string }): { base64: string };
  request(capability: "file.list", payload: { path: string }): Array<{ name: string; isDirectory: boolean }>;
  request(capability: "data.read", payload: { path: string }): { base64: string };
  request(capability: "data.write", payload: { path: string; base64: string }): { written: number };
  request(capability: "data.exists", payload: { path: string }): { exists: boolean; isDirectory: boolean };
  request(capability: "data.info", payload: { path: string }): PluginDataInfo;
  request(capability: "data.list", payload: { path: string }): PluginDataEntry[];
}

export interface PluginDataEntry {
  name: string;
  isDirectory: boolean;
  length?: number;
  lastModifiedUtc?: string;
}

export interface PluginDataInfo {
  path: string;
  fileName: string;
  isDirectory: boolean;
  length?: number;
  lastModifiedUtc?: string;
}

export interface SdwPlugin {
  handlers: Record<string, (input: unknown, configuration: Readonly<Record<string, unknown>>) => unknown>;
}

declare global {
  const sdw: Readonly<SdwPluginHost>;
  var sdwPlugin: SdwPlugin;
}
