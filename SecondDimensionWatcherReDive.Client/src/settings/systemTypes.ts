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

export interface OpenAiSettings {
  baseUrl: string;
  apiMode: "responses" | "chatCompletions";
  model: string;
  maxTokens: number;
  apiKey: SecretState;
}

export interface AnthropicSettings {
  baseUrl: string;
  model: string;
  maxTokens: number;
  apiVersion: string;
  apiKey: SecretState;
}

export interface CodexAppServerSettings {
  endpoint: string;
  model: string;
  permissionProfile: string;
  timeoutSeconds: number;
  token: SecretState;
}

export interface AiSettings {
  executionMode: "builtIn" | "codexAppServer";
  provider: "openAI" | "anthropic";
  openAI: OpenAiSettings;
  anthropic: AnthropicSettings;
  codexAppServer: CodexAppServerSettings;
  inference: {
    rateLimitDelayMs: number;
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

export interface OpenAiSettingsPatch extends Omit<OpenAiSettings, "apiKey"> {
  apiKey?: SecretMutation | null;
}

export interface AnthropicSettingsPatch extends Omit<
  AnthropicSettings,
  "apiKey"
> {
  apiKey?: SecretMutation | null;
}

export interface CodexAppServerSettingsPatch extends Omit<
  CodexAppServerSettings,
  "token"
> {
  token?: SecretMutation | null;
}

export interface AiSettingsPatch extends Omit<
  AiSettings,
  "openAI" | "anthropic" | "codexAppServer"
> {
  openAI: OpenAiSettingsPatch;
  anthropic: AnthropicSettingsPatch;
  codexAppServer: CodexAppServerSettingsPatch;
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
  "webhookUrl"
> {
  webhookUrl?: SecretMutation | null;
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
