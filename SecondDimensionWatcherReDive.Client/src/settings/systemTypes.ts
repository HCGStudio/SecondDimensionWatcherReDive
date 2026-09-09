export type SecretSource = "runtime" | "deployment" | "none";

export interface SecretState {
  isConfigured: boolean;
  source: SecretSource;
}

export type SecretOperation = "keep" | "set" | "clear" | "reset";

export interface SecretMutation {
  operation: SecretOperation;
  value?: string;
}

export interface SecretDraft {
  operation: Exclude<SecretOperation, "set">;
  value: string;
}

export type AiProtocol =
  "openAIResponses" | "openAIChatCompletions" | "anthropic" | "codexAppServer";

export interface AiProviderModel {
  id: string;
  name: string;
  reasoningEfforts: string[];
}

export interface AiProviderSettings {
  id: string;
  name: string;
  protocol: AiProtocol;
  baseUrl: string;
  model: string;
  maxTokens: number;
  apiVersion: string;
  reasoningEffort: string | null;
  models: AiProviderModel[];
  apiKey: SecretState;
  endpoint: string;
  permissionProfile: string;
  timeoutSeconds: number;
  token: SecretState;
}

export interface CodexAppServerSettings {
  endpoint: string;
  model: string;
  permissionProfile: string;
  timeoutSeconds: number;
  token: SecretState;
}

export interface AiSettings {
  defaultProviderId: string | null;
  providers: AiProviderSettings[];
  inference: {
    rateLimitDelayMs: number;
    providerId: string | null;
    model: string | null;
    reasoningEffort: string | null;
  };
}

export interface TmdbSettings {
  apiKey: SecretState;
}

export interface TorrentSettings {
  url: string;
  userName: string;
  userAgent: string;
  password: SecretState;
}

export interface MediaLibrarySettings {
  allowedRoots: string[];
  scanInterval: string;
  settlingPeriod: string;
  missingGracePeriod: string;
}

export interface IncidentSettings {
  downloadStalledAfter: string;
  reportThrottle: string;
  reconciliationInterval: string;
  disk: {
    minimumAvailableBytes: number;
    minimumAvailablePercent: number;
  };
}

export interface NfsSettings {
  enabled: boolean;
  port: number;
  bindAddress: string;
  leaseSeconds: number;
  maxConnections: number;
  idleTimeoutSeconds: number;
  allowAnonymous: boolean;
  allowedNetworks: string[];
  restartRequired: boolean;
  pendingRestart: boolean;
}

export type NotificationEventType =
  | "releaseMatched"
  | "downloadPendingConfirmation"
  | "downloadCompleted"
  | "downloadFailed"
  | "incidentOpened"
  | "metadataNeedsReview"
  | "diskSpaceLow";

export interface NotificationSettings {
  webhookEnabled: boolean;
  webPushEnabled: boolean;
  webPushSubject: string;
  vapidPublicKey: string;
  vapidPrivateKey: SecretState;
  events: NotificationEventType[];
  quietHoursStart: string | null;
  quietHoursEnd: string | null;
  timeZoneId: string;
  webhookUrl: SecretState;
}

export interface SystemSettings {
  revision: number;
  pendingRestart: boolean;
  ai: AiSettings;
  tmdb: TmdbSettings;
  torrent: TorrentSettings;
  mediaLibrary: MediaLibrarySettings;
  incidents: IncidentSettings;
  nfs: NfsSettings;
  notifications: NotificationSettings;
}

export interface AiProviderSettingsPatch extends Omit<
  AiProviderSettings,
  "apiKey" | "token"
> {
  apiKey?: SecretMutation | null;
  token?: SecretMutation | null;
}

export interface CodexAppServerSettingsPatch extends Omit<
  CodexAppServerSettings,
  "token"
> {
  token?: SecretMutation | null;
}

export interface AiSettingsPatch extends Omit<AiSettings, "providers"> {
  providers: AiProviderSettingsPatch[];
}

export interface TmdbSettingsPatch {
  apiKey?: SecretMutation | null;
}

export interface TorrentSettingsPatch extends Omit<
  TorrentSettings,
  "password"
> {
  password?: SecretMutation | null;
}

export type NfsSettingsPatch = Omit<
  NfsSettings,
  "restartRequired" | "pendingRestart"
>;

export interface NotificationSettingsPatch extends Omit<
  NotificationSettings,
  "webhookUrl" | "vapidPublicKey" | "vapidPrivateKey"
> {
  webhookUrl?: SecretMutation | null;
  generateVapidKeys?: boolean;
}

export interface SystemSettingsPatch {
  expectedRevision: number;
  ai?: AiSettingsPatch;
  tmdb?: TmdbSettingsPatch;
  torrent?: TorrentSettingsPatch;
  mediaLibrary?: MediaLibrarySettings;
  incidents?: IncidentSettings;
  nfs?: NfsSettingsPatch;
  notifications?: NotificationSettingsPatch;
}

export const createSecretDraft = (): SecretDraft => ({
  operation: "keep",
  value: "",
});

export const toSecretMutation = (draft: SecretDraft): SecretMutation | null => {
  if (draft.value.trim()) return { operation: "set", value: draft.value };
  if (draft.operation === "keep") return null;
  return { operation: draft.operation };
};
