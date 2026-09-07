export interface PluginCapabilities {
  networkDomains: string[];
  fileRoots: string[];
  notifications: boolean;
  downloadControl: boolean;
  storageAccess: boolean;
  backgroundTasks: boolean;
}

export interface PluginManifest {
  id: string;
  name: string;
  version: string;
  apiVersion: string;
  entryPoint: string;
  description?: string;
  dependencies: { id: string; minimumVersion: string }[];
  capabilities: PluginCapabilities;
  platforms: string[];
  fileSha256: Record<string, string>;
  signaturePublisher?: string;
  signatureAlgorithm?: string;
  providers: { kind: string; name: string; handlers: Record<string, string> }[];
  dataVersion: number;
  dataMigration?: { strategy: string; description?: string };
}

export interface PluginPackagePreview {
  token: string;
  packageSha256: string;
  manifest: PluginManifest;
  compatibilityErrors: string[];
  isSignatureTrusted: boolean;
  signatureStatus: string;
  expiresAt: string;
}

export interface InstalledPlugin {
  manifest: PluginManifest;
  isEnabled: boolean;
  approvedCapabilities: PluginCapabilities;
  compatibilityErrors: string[];
  health: {
    status: string;
    consecutiveFailures: number;
    lastSuccessAt?: string;
    lastFailureAt?: string;
    lastError?: string;
    circuitOpenUntil?: string;
  };
  hasConfiguration: boolean;
}
