import React from "react";
import { useTranslation } from "react-i18next";
import { useSWRConfig } from "swr";

import { Bot, Cloud, ServerCog, SquareTerminal } from "lucide-react";

import {
  AiSettings,
  AiSettingsPatch,
  SecretDraft,
  SystemSettings,
  createSecretDraft,
  toSecretMutation,
} from "../../settings/systemTypes";
import {
  isCodexEndpoint,
  isHttpEndpoint,
  isIntegerInRange,
  isRemoteWebSocketEndpoint,
  requiresCredentialChange,
  willSecretBeConfigured,
} from "../../settings/validation";
import { useToast } from "../ToastProvider";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import {
  SecretField,
  Select,
  SettingsSaveBar,
  SettingsSectionHeader,
} from "./SettingsControls";

interface AiDraft extends AiSettings {
  openAiApiKey: SecretDraft;
  anthropicApiKey: SecretDraft;
  codexToken: SecretDraft;
}

const createDraft = (value: AiSettings): AiDraft => ({
  ...value,
  openAI: { ...value.openAI },
  anthropic: { ...value.anthropic },
  codexAppServer: { ...value.codexAppServer },
  inference: { ...value.inference },
  openAiApiKey: createSecretDraft(),
  anthropicApiKey: createSecretDraft(),
  codexToken: createSecretDraft(),
});

const isSecretDirty = (draft: SecretDraft) =>
  draft.operation !== "keep" || draft.value.trim().length > 0;

export interface AiSettingsSectionProps {
  value: AiSettings;
  onSave: (patch: { ai: AiSettingsPatch }) => Promise<SystemSettings>;
}

export const AiSettingsSection: React.FC<AiSettingsSectionProps> = ({
  value,
  onSave,
}) => {
  const { t } = useTranslation("settings");
  const { addToast } = useToast();
  const { mutate: mutateGlobal } = useSWRConfig();
  const [draft, setDraft] = React.useState<AiDraft>(() => createDraft(value));
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => {
    setDraft(createDraft(value));
  }, [value]);

  const plainDraft: AiSettings = {
    executionMode: draft.executionMode,
    provider: draft.provider,
    openAI: draft.openAI,
    anthropic: draft.anthropic,
    codexAppServer: draft.codexAppServer,
    inference: draft.inference,
  };
  const dirty =
    JSON.stringify(plainDraft) !== JSON.stringify(value) ||
    isSecretDirty(draft.openAiApiKey) ||
    isSecretDirty(draft.anthropicApiKey) ||
    isSecretDirty(draft.codexToken);

  const selectedBuiltIn =
    draft.provider === "openAI" ? draft.openAI : draft.anthropic;
  const openAiCredentialRequired = requiresCredentialChange(
    value.openAI.baseUrl,
    draft.openAI.baseUrl,
    value.openAI.apiKey,
    draft.openAiApiKey,
  );
  const anthropicCredentialRequired = requiresCredentialChange(
    value.anthropic.baseUrl,
    draft.anthropic.baseUrl,
    value.anthropic.apiKey,
    draft.anthropicApiKey,
  );
  const codexCredentialRequired = requiresCredentialChange(
    value.codexAppServer.endpoint,
    draft.codexAppServer.endpoint,
    value.codexAppServer.token,
    draft.codexToken,
  );
  const codexEndpoint = draft.codexAppServer.endpoint.trim();
  const remoteCodexTokenRequired =
    isRemoteWebSocketEndpoint(codexEndpoint) &&
    !willSecretBeConfigured(value.codexAppServer.token, draft.codexToken);
  const invalid =
    !isIntegerInRange(draft.inference.rateLimitDelayMs, 0, 2_147_483_647) ||
    !isHttpEndpoint(draft.openAI.baseUrl) ||
    !draft.openAI.model.trim() ||
    !isIntegerInRange(draft.openAI.maxTokens, 1, 2_147_483_647) ||
    !isHttpEndpoint(draft.anthropic.baseUrl) ||
    !draft.anthropic.model.trim() ||
    !draft.anthropic.apiVersion.trim() ||
    !isIntegerInRange(draft.anthropic.maxTokens, 1, 2_147_483_647) ||
    !draft.codexAppServer.permissionProfile.trim() ||
    !isIntegerInRange(draft.codexAppServer.timeoutSeconds, 1, 3600) ||
    (!!codexEndpoint && !isCodexEndpoint(codexEndpoint)) ||
    openAiCredentialRequired ||
    anthropicCredentialRequired ||
    codexCredentialRequired ||
    remoteCodexTokenRequired ||
    (draft.executionMode === "codexAppServer"
      ? !codexEndpoint
      : !selectedBuiltIn.baseUrl.trim());

  const reset = React.useCallback(() => {
    setDraft(createDraft(value));
    setSaved(false);
  }, [value]);

  const save = React.useCallback(async () => {
    if (saving || invalid) {
      if (invalid) {
        addToast({
          title: t("system.ai.validationFailed"),
          color: "warning",
        });
      }
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      const patch: AiSettingsPatch = {
        executionMode: draft.executionMode,
        provider: draft.provider,
        openAI: {
          baseUrl: draft.openAI.baseUrl.trim(),
          apiMode: draft.openAI.apiMode,
          model: draft.openAI.model.trim(),
          maxTokens: draft.openAI.maxTokens,
          apiKey: toSecretMutation(draft.openAiApiKey),
        },
        anthropic: {
          baseUrl: draft.anthropic.baseUrl.trim(),
          model: draft.anthropic.model.trim(),
          maxTokens: draft.anthropic.maxTokens,
          apiVersion: draft.anthropic.apiVersion.trim(),
          apiKey: toSecretMutation(draft.anthropicApiKey),
        },
        codexAppServer: {
          endpoint: draft.codexAppServer.endpoint.trim(),
          model: draft.codexAppServer.model.trim(),
          permissionProfile: draft.codexAppServer.permissionProfile.trim(),
          timeoutSeconds: draft.codexAppServer.timeoutSeconds,
          token: toSecretMutation(draft.codexToken),
        },
        inference: { ...draft.inference },
      };
      await onSave({ ai: patch });
      await Promise.allSettled([
        mutateGlobal("/api/chat/status"),
        mutateGlobal("/api/chat/models"),
      ]);
      setSaved(true);
      addToast({ title: t("system.ai.saved"), color: "success" });
    } catch (error) {
      addToast({
        title:
          error instanceof Error && error.message === "409"
            ? t("system.save.conflict")
            : t("system.save.failed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  }, [addToast, draft, invalid, mutateGlobal, onSave, saving, t]);

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("system.ai.eyebrow")}
        title={t("system.ai.title")}
        description={t("system.ai.description")}
      />

      <Card icon={<Bot size={18} />} title={t("system.ai.engine.title")}>
        <fieldset className="grid gap-3 md:grid-cols-2">
          <legend className="sr-only">{t("system.ai.engine.title")}</legend>
          <EngineOption
            icon={<Cloud size={19} />}
            title={t("system.ai.engine.builtIn.title")}
            description={t("system.ai.engine.builtIn.description")}
            selected={draft.executionMode === "builtIn"}
            onSelect={() =>
              setDraft((current) => ({
                ...current,
                executionMode: "builtIn",
              }))
            }
          />
          <EngineOption
            icon={<SquareTerminal size={19} />}
            title={t("system.ai.engine.codex.title")}
            description={t("system.ai.engine.codex.description")}
            selected={draft.executionMode === "codexAppServer"}
            onSelect={() =>
              setDraft((current) => ({
                ...current,
                executionMode: "codexAppServer",
              }))
            }
          />
        </fieldset>
        <p className="mt-3 text-xs leading-body text-subtle">
          {t("system.ai.engine.scope")}
        </p>
      </Card>

      {draft.executionMode === "builtIn" ? (
        <Card
          className="mt-5"
          icon={<ServerCog size={18} />}
          title={t("system.ai.builtIn.title")}
          description={t("system.ai.builtIn.description")}
        >
          <div className="grid gap-5 sm:grid-cols-2">
            <FormRow label={t("system.ai.builtIn.provider")}>
              <Select
                value={draft.provider}
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    provider: event.target.value as AiSettings["provider"],
                  }))
                }
              >
                <option value="openAI">OpenAI</option>
                <option value="anthropic">Anthropic</option>
              </Select>
            </FormRow>
            {draft.provider === "openAI" ? (
              <FormRow label={t("system.ai.builtIn.apiMode")}>
                <Select
                  value={draft.openAI.apiMode}
                  onChange={(event) =>
                    setDraft((current) => ({
                      ...current,
                      openAI: {
                        ...current.openAI,
                        apiMode: event.target
                          .value as AiSettings["openAI"]["apiMode"],
                      },
                    }))
                  }
                >
                  <option value="responses">Responses</option>
                  <option value="chatCompletions">Chat Completions</option>
                </Select>
              </FormRow>
            ) : (
              <FormRow label={t("system.ai.builtIn.apiVersion")}>
                <Input
                  value={draft.anthropic.apiVersion}
                  isInvalid={!draft.anthropic.apiVersion.trim()}
                  onChange={(event) =>
                    setDraft((current) => ({
                      ...current,
                      anthropic: {
                        ...current.anthropic,
                        apiVersion: event.target.value,
                      },
                    }))
                  }
                />
              </FormRow>
            )}
          </div>

          {draft.provider === "openAI" ? (
            <ProviderFields
              prefix="openai"
              baseUrl={draft.openAI.baseUrl}
              model={draft.openAI.model}
              maxTokens={draft.openAI.maxTokens}
              credentialRequired={openAiCredentialRequired}
              onBaseUrlChange={(baseUrl) =>
                setDraft((current) => ({
                  ...current,
                  openAI: { ...current.openAI, baseUrl },
                }))
              }
              onModelChange={(model) =>
                setDraft((current) => ({
                  ...current,
                  openAI: { ...current.openAI, model },
                }))
              }
              onMaxTokensChange={(maxTokens) =>
                setDraft((current) => ({
                  ...current,
                  openAI: { ...current.openAI, maxTokens },
                }))
              }
              secret={
                <SecretField
                  id="settings-openai-api-key"
                  label={t("system.ai.builtIn.apiKey")}
                  state={value.openAI.apiKey}
                  draft={draft.openAiApiKey}
                  onChange={(openAiApiKey) =>
                    setDraft((current) => ({ ...current, openAiApiKey }))
                  }
                />
              }
            />
          ) : (
            <ProviderFields
              prefix="anthropic"
              baseUrl={draft.anthropic.baseUrl}
              model={draft.anthropic.model}
              maxTokens={draft.anthropic.maxTokens}
              credentialRequired={anthropicCredentialRequired}
              onBaseUrlChange={(baseUrl) =>
                setDraft((current) => ({
                  ...current,
                  anthropic: { ...current.anthropic, baseUrl },
                }))
              }
              onModelChange={(model) =>
                setDraft((current) => ({
                  ...current,
                  anthropic: { ...current.anthropic, model },
                }))
              }
              onMaxTokensChange={(maxTokens) =>
                setDraft((current) => ({
                  ...current,
                  anthropic: { ...current.anthropic, maxTokens },
                }))
              }
              secret={
                <SecretField
                  id="settings-anthropic-api-key"
                  label={t("system.ai.builtIn.apiKey")}
                  state={value.anthropic.apiKey}
                  draft={draft.anthropicApiKey}
                  onChange={(anthropicApiKey) =>
                    setDraft((current) => ({ ...current, anthropicApiKey }))
                  }
                />
              }
            />
          )}
        </Card>
      ) : (
        <Card
          className="mt-5"
          icon={<SquareTerminal size={18} />}
          title={t("system.ai.codex.title")}
          description={t("system.ai.codex.description")}
        >
          <div className="grid gap-5 sm:grid-cols-2">
            <FormRow label={t("system.ai.codex.endpoint")}>
              <Input
                type="url"
                value={draft.codexAppServer.endpoint}
                placeholder="ws://127.0.0.1:4500"
                isInvalid={
                  !!draft.codexAppServer.endpoint &&
                  !isCodexEndpoint(draft.codexAppServer.endpoint)
                }
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    codexAppServer: {
                      ...current.codexAppServer,
                      endpoint: event.target.value,
                    },
                  }))
                }
              />
              {draft.codexAppServer.endpoint &&
              !isCodexEndpoint(draft.codexAppServer.endpoint) ? (
                <p className="mt-1 text-xs text-error">
                  {t("system.ai.codex.endpointError")}
                </p>
              ) : null}
              {codexCredentialRequired ? (
                <p className="mt-1 text-xs text-warning">
                  {t("system.ai.originCredentialRequired")}
                </p>
              ) : null}
            </FormRow>
            <FormRow label={t("system.ai.codex.model")}>
              <Input
                value={draft.codexAppServer.model}
                placeholder={t("system.ai.codex.modelPlaceholder")}
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    codexAppServer: {
                      ...current.codexAppServer,
                      model: event.target.value,
                    },
                  }))
                }
              />
              <p className="mt-1 text-xs leading-body text-subtle">
                {t("system.ai.codex.modelHelp")}
              </p>
            </FormRow>
            <FormRow label={t("system.ai.codex.permissionProfile")}>
              <Input
                value={draft.codexAppServer.permissionProfile}
                placeholder=":read-only"
                isInvalid={!draft.codexAppServer.permissionProfile.trim()}
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    codexAppServer: {
                      ...current.codexAppServer,
                      permissionProfile: event.target.value,
                    },
                  }))
                }
              />
              <p className="mt-1 text-xs leading-body text-subtle">
                {t("system.ai.codex.permissionProfileHelp")}
              </p>
            </FormRow>
            <FormRow label={t("system.ai.codex.timeout")}>
              <Input
                type="number"
                min={1}
                max={3600}
                value={draft.codexAppServer.timeoutSeconds}
                isInvalid={
                  !isIntegerInRange(
                    draft.codexAppServer.timeoutSeconds,
                    1,
                    3600,
                  )
                }
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    codexAppServer: {
                      ...current.codexAppServer,
                      timeoutSeconds: Number(event.target.value),
                    },
                  }))
                }
              />
            </FormRow>
            <SecretField
              id="settings-codex-token"
              label={t("system.ai.codex.token")}
              state={value.codexAppServer.token}
              draft={draft.codexToken}
              onChange={(codexToken) =>
                setDraft((current) => ({ ...current, codexToken }))
              }
            />
            {remoteCodexTokenRequired ? (
              <p className="text-xs text-warning">
                {t("system.ai.codex.remoteTokenRequired")}
              </p>
            ) : null}
          </div>
          <div className="mt-4 rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-xs leading-body text-muted">
            {t("system.ai.codex.serverAddressHelp")}
          </div>
        </Card>
      )}

      <Card className="mt-5" title={t("system.ai.inference.title")}>
        <div className="max-w-sm">
          <FormRow label={t("system.ai.inference.rateLimitDelay")}>
            <Input
              type="number"
              min={0}
              max={2_147_483_647}
              value={draft.inference.rateLimitDelayMs}
              isInvalid={
                !isIntegerInRange(
                  draft.inference.rateLimitDelayMs,
                  0,
                  2_147_483_647,
                )
              }
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  inference: {
                    rateLimitDelayMs: Number(event.target.value),
                  },
                }))
              }
            />
          </FormRow>
        </div>
      </Card>

      <SettingsSaveBar
        dirty={dirty}
        saving={saving}
        saved={saved}
        onReset={reset}
        onSave={() => void save()}
      />
    </section>
  );
};

interface EngineOptionProps {
  icon: React.ReactNode;
  title: string;
  description: string;
  selected: boolean;
  onSelect: () => void;
}

const EngineOption: React.FC<EngineOptionProps> = ({
  icon,
  title,
  description,
  selected,
  onSelect,
}) => (
  <label
    className={`cursor-pointer rounded-lg border p-4 transition-colors ${
      selected
        ? "border-brand bg-brand/5 shadow-ring-brand"
        : "border-border bg-surface hover:border-ring-deep"
    }`}
  >
    <input
      type="radio"
      name="ai-execution-mode"
      checked={selected}
      className="sr-only"
      onChange={onSelect}
    />
    <span className={selected ? "text-brand" : "text-muted"}>{icon}</span>
    <span className="mt-3 block text-sm font-medium text-foreground">
      {title}
    </span>
    <span className="mt-1 block text-xs leading-body text-muted">
      {description}
    </span>
  </label>
);

interface ProviderFieldsProps {
  prefix: string;
  baseUrl: string;
  model: string;
  maxTokens: number;
  credentialRequired: boolean;
  onBaseUrlChange: (value: string) => void;
  onModelChange: (value: string) => void;
  onMaxTokensChange: (value: number) => void;
  secret: React.ReactNode;
}

const ProviderFields: React.FC<ProviderFieldsProps> = ({
  prefix,
  baseUrl,
  model,
  maxTokens,
  credentialRequired,
  onBaseUrlChange,
  onModelChange,
  onMaxTokensChange,
  secret,
}) => {
  const { t } = useTranslation("settings");
  return (
    <div className="mt-5 grid gap-5 sm:grid-cols-2">
      <FormRow label={t("system.ai.builtIn.baseUrl")}>
        <Input
          id={`settings-${prefix}-base-url`}
          type="url"
          value={baseUrl}
          isInvalid={!isHttpEndpoint(baseUrl)}
          onChange={(event) => onBaseUrlChange(event.target.value)}
        />
        {credentialRequired ? (
          <p className="mt-1 text-xs text-warning">
            {t("system.ai.originCredentialRequired")}
          </p>
        ) : null}
      </FormRow>
      <FormRow label={t("system.ai.builtIn.model")}>
        <Input
          id={`settings-${prefix}-model`}
          value={model}
          isInvalid={!model.trim()}
          onChange={(event) => onModelChange(event.target.value)}
        />
      </FormRow>
      <FormRow label={t("system.ai.builtIn.maxTokens")}>
        <Input
          id={`settings-${prefix}-max-tokens`}
          type="number"
          min={1}
          max={2_147_483_647}
          value={maxTokens}
          isInvalid={!isIntegerInRange(maxTokens, 1, 2_147_483_647)}
          onChange={(event) => onMaxTokensChange(Number(event.target.value))}
        />
      </FormRow>
      {secret}
    </div>
  );
};
