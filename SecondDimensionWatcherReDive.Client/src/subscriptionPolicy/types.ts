export type SubscriptionPolicyMode =
  "NotifyOnly" | "ManualConfirm" | "AutoDownload";

export interface ISubscriptionPolicyDraft {
  subtitleGroups: string[];
  resolutions: string[];
  codecs: string[];
  languages: string[];
  minSizeBytes: number | null;
  maxSizeBytes: number | null;
  excludedKeywords: string[];
  mode: SubscriptionPolicyMode;
  enableVersionUpgrade: boolean;
  minimumUpgradeScore: number;
  upgradeRollbackHours: number;
}

export interface ISubscriptionPolicy extends ISubscriptionPolicyDraft {
  feedId: string;
  updatedAt?: string;
}

export interface ISubscriptionPolicyExplanation {
  field: string;
  passed: boolean;
  actual?: string | null;
  expected?: string | null;
  message: string;
}

export interface ISubscriptionPolicySimulationEntry {
  id: string;
  title: string;
  publishedAt: string;
  sizeBytes: number | null;
  matched: boolean;
  explanations: ISubscriptionPolicyExplanation[];
}

export interface ISubscriptionPolicySimulation {
  total: number;
  matched: number;
  entries: ISubscriptionPolicySimulationEntry[];
}

export const createEmptySubscriptionPolicy = (): ISubscriptionPolicyDraft => ({
  subtitleGroups: [],
  resolutions: [],
  codecs: [],
  languages: [],
  minSizeBytes: null,
  maxSizeBytes: null,
  excludedKeywords: [],
  mode: "ManualConfirm",
  enableVersionUpgrade: false,
  minimumUpgradeScore: 25,
  upgradeRollbackHours: 72,
});

export const toSubscriptionPolicyDraft = (
  policy: ISubscriptionPolicy,
): ISubscriptionPolicyDraft => ({
  subtitleGroups: [...policy.subtitleGroups],
  resolutions: [...policy.resolutions],
  codecs: [...policy.codecs],
  languages: [...policy.languages],
  minSizeBytes: policy.minSizeBytes,
  maxSizeBytes: policy.maxSizeBytes,
  excludedKeywords: [...policy.excludedKeywords],
  mode: policy.mode,
  enableVersionUpgrade: policy.enableVersionUpgrade ?? false,
  minimumUpgradeScore: policy.minimumUpgradeScore ?? 25,
  upgradeRollbackHours: policy.upgradeRollbackHours ?? 72,
});
