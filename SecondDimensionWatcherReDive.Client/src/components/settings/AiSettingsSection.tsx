import React from "react";
import { useTranslation } from "react-i18next";
import { useSWRConfig } from "swr";

import { Bot, Plus, ServerCog, Trash2 } from "lucide-react";

import { useChatModels } from "../../chat/hooks";
import { AiModel } from "../../chat/types";
import { apiErrorStatus } from "../../errors/apiError";
import {
  AiProtocol,
  AiProviderSettings,
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
import { ModelPicker } from "../chat/ModelPicker";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import {
  SecretField,
  Select,
  SettingsSaveBar,
  SettingsSectionHeader,
} from "./SettingsControls";

interface ProviderDraft extends AiProviderSettings {
  apiKeyDraft: SecretDraft;
  tokenDraft: SecretDraft;
}
interface AiDraft extends Omit<AiSettings, "providers"> {
  providers: ProviderDraft[];
}

const emptySecret = { isConfigured: false, source: "none" as const };
const createDraft = (value: AiSettings): AiDraft => ({
  defaultProviderId: value.defaultProviderId,
  providers: value.providers.map((provider) => ({
    ...provider,
    models: provider.models.map((model) => ({
      ...model,
      name: model.name ?? model.id,
      reasoningEfforts: [...model.reasoningEfforts],
    })),
    apiKeyDraft: createSecretDraft(),
    tokenDraft: createSecretDraft(),
  })),
  inference: { ...value.inference },
});
const isSecretDirty = (draft: SecretDraft) =>
  draft.operation !== "keep" || !!draft.value.trim();
const plainProvider = ({
  apiKeyDraft: _apiKeyDraft,
  tokenDraft: _tokenDraft,
  ...provider
}: ProviderDraft): AiProviderSettings => provider;

const defaultEfforts = (protocol: AiProtocol, model: string): string[] => {
  if (protocol === "anthropic" && model === "claude-sonnet-5")
    return ["low", "medium", "high", "xhigh", "max"];
  if (protocol !== "anthropic" && model === "gpt-5.6-luna")
    return ["none", "low", "medium", "high", "xhigh", "max"];
  return [];
};

const chatCompletionsDefaultEffort = (
  protocol: AiProtocol,
  model: string,
): string | null =>
  protocol === "openAIChatCompletions" &&
  /^gpt-5\.6-(?:luna|sol|terra)(?:-|$)/.test(model)
    ? "none"
    : null;

const protocolDefaults = (
  protocol: AiProtocol,
): Partial<AiProviderSettings> => {
  if (protocol === "anthropic")
    return { baseUrl: "https://api.anthropic.com", model: "claude-sonnet-5" };
  if (protocol === "codexAppServer") return { model: "" };
  return { baseUrl: "https://api.openai.com/v1", model: "gpt-5.6-luna" };
};

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
  const { data: availableModels } = useChatModels();
  const [draft, setDraft] = React.useState<AiDraft>(() => createDraft(value));
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);
  React.useEffect(() => setDraft(createDraft(value)), [value]);

  const plainDraft: AiSettings = {
    defaultProviderId: draft.defaultProviderId,
    providers: draft.providers.map(plainProvider),
    inference: draft.inference,
  };
  const original: AiSettings = {
    defaultProviderId: value.defaultProviderId,
    providers: value.providers,
    inference: value.inference,
  };
  const dirty =
    JSON.stringify(plainDraft) !== JSON.stringify(original) ||
    draft.providers.some(
      (provider) =>
        isSecretDirty(provider.apiKeyDraft) ||
        isSecretDirty(provider.tokenDraft),
    );
  const credentialRequired = (provider: ProviderDraft) => {
    const previous = value.providers.find((item) => item.id === provider.id);
    return (
      !!previous &&
      (requiresCredentialChange(
        previous.protocol === "codexAppServer"
          ? previous.endpoint
          : previous.baseUrl,
        provider.protocol === "codexAppServer"
          ? provider.endpoint
          : provider.baseUrl,
        previous.apiKey,
        provider.apiKeyDraft,
      ) ||
        requiresCredentialChange(
          previous.protocol === "codexAppServer"
            ? previous.endpoint
            : previous.baseUrl,
          provider.protocol === "codexAppServer"
            ? provider.endpoint
            : provider.baseUrl,
          previous.token,
          provider.tokenDraft,
        ))
    );
  };
  const invalid =
    !isIntegerInRange(draft.inference.rateLimitDelayMs, 0, 2_147_483_647) ||
    (draft.providers.length > 0 &&
      !draft.providers.some(
        (provider) => provider.id === draft.defaultProviderId,
      )) ||
    (!!draft.inference.providerId &&
      !draft.providers.some(
        (provider) => provider.id === draft.inference.providerId,
      )) ||
    draft.providers.some(
      (provider) =>
        !provider.name.trim() ||
        credentialRequired(provider) ||
        new Set(provider.models.map((model) => model.id.trim())).size !==
          provider.models.length ||
        provider.models.some((model) => !model.id.trim()) ||
        (provider.protocol === "codexAppServer"
          ? !isCodexEndpoint(provider.endpoint) ||
            !provider.permissionProfile.trim() ||
            !isIntegerInRange(provider.timeoutSeconds, 1, 3600) ||
            (isRemoteWebSocketEndpoint(provider.endpoint) &&
              !willSecretBeConfigured(provider.token, provider.tokenDraft))
          : !isHttpEndpoint(provider.baseUrl) ||
            !provider.model.trim() ||
            !isIntegerInRange(provider.maxTokens, 1, 2_147_483_647) ||
            (provider.protocol === "anthropic" && !provider.apiVersion.trim())),
    );

  const updateProvider = (id: string, change: Partial<ProviderDraft>) =>
    setDraft((current) => ({
      ...current,
      providers: current.providers.map((provider) =>
        provider.id === id ? { ...provider, ...change } : provider,
      ),
    }));
  const addProvider = () => {
    const suffix =
      typeof crypto.randomUUID === "function"
        ? crypto.randomUUID()
        : Array.from(crypto.getRandomValues(new Uint8Array(16)), (value) =>
            value.toString(16).padStart(2, "0"),
          ).join("");
    const id = `provider-${suffix}`;
    setDraft((current) => ({
      ...current,
      defaultProviderId: current.defaultProviderId || id,
      providers: [
        ...current.providers,
        {
          id,
          name: t("system.ai.providers.newName", {
            count: current.providers.length + 1,
          }),
          protocol: "openAIResponses",
          baseUrl: "https://api.openai.com/v1",
          model: "gpt-5.6-luna",
          maxTokens: 16384,
          apiVersion: "2023-06-01",
          reasoningEffort: null,
          models: [],
          endpoint: "ws://127.0.0.1:4500",
          permissionProfile: ":read-only",
          timeoutSeconds: 120,
          apiKey: emptySecret,
          token: emptySecret,
          apiKeyDraft: createSecretDraft(),
          tokenDraft: createSecretDraft(),
        },
      ],
    }));
  };
  const removeProvider = (id: string) =>
    setDraft((current) => {
      const providers = current.providers.filter(
        (provider) => provider.id !== id,
      );
      return {
        ...current,
        providers,
        defaultProviderId:
          current.defaultProviderId === id
            ? (providers[0]?.id ?? null)
            : current.defaultProviderId,
        inference:
          current.inference.providerId === id
            ? {
                ...current.inference,
                providerId: null,
                model: null,
                reasoningEffort: null,
              }
            : current.inference,
      };
    });

  const models: AiModel[] = draft.providers.flatMap((provider) => {
    const discovered = (availableModels ?? []).filter(
      (model) => model.providerId === provider.id,
    );
    const options = new Map(discovered.map((model) => [model.id, model]));
    for (const model of provider.models)
      options.set(model.id, {
        ...model,
        provider: provider.name,
        providerId: provider.id,
      });
    if (!options.has(provider.model))
      options.set(provider.model, {
        id: provider.model,
        name: provider.model || t("system.ai.codex.modelPlaceholder"),
        providerId: provider.id,
        provider: provider.name,
        reasoningEfforts: defaultEfforts(provider.protocol, provider.model),
      });
    return [...options.values()]
      .sort(
        (a, b) =>
          Number(b.id === provider.model) - Number(a.id === provider.model),
      )
      .map((model) => ({ ...model, provider: provider.name }));
  });

  const save = async () => {
    if (saving) return;
    if (invalid) {
      addToast({ title: t("system.ai.validationFailed"), color: "warning" });
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      await onSave({
        ai: {
          defaultProviderId: draft.defaultProviderId,
          providers: draft.providers.map((provider) => ({
            ...plainProvider(provider),
            name: provider.name.trim(),
            baseUrl: provider.baseUrl.trim(),
            model: provider.model.trim(),
            endpoint: provider.endpoint.trim(),
            permissionProfile: provider.permissionProfile.trim(),
            apiVersion: provider.apiVersion.trim(),
            models: provider.models.map((model) => ({
              ...model,
              id: model.id.trim(),
              name: model.name.trim() || model.id.trim(),
              reasoningEfforts: [
                ...new Set(
                  model.reasoningEfforts
                    .map((effort) => effort.trim())
                    .filter(Boolean),
                ),
              ],
            })),
            apiKey: toSecretMutation(provider.apiKeyDraft),
            token: toSecretMutation(provider.tokenDraft),
          })),
          inference: {
            ...draft.inference,
            model: draft.inference.model?.trim() || null,
          },
        },
      });
      await Promise.allSettled([
        mutateGlobal("/api/chat/status"),
        mutateGlobal("/api/chat/models"),
      ]);
      setSaved(true);
      addToast({ title: t("system.ai.saved"), color: "success" });
    } catch (error) {
      addToast({
        title:
          apiErrorStatus(error) === 409
            ? t("system.save.conflict")
            : t("system.save.failed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("system.ai.eyebrow")}
        title={t("system.ai.title")}
        description={t("system.ai.providers.description")}
      />
      <Card icon={<Bot size={18} />} title={t("system.ai.providers.title")}>
        <div className="flex flex-wrap items-end gap-4">
          <FormRow
            label={t("system.ai.providers.default")}
            className="min-w-48 flex-1"
          >
            <Select
              value={draft.defaultProviderId ?? ""}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  defaultProviderId: event.target.value || null,
                }))
              }
            >
              {!draft.providers.length && (
                <option value="">{t("system.ai.providers.empty")}</option>
              )}
              {draft.providers.map((provider) => (
                <option key={provider.id} value={provider.id}>
                  {provider.name} · {provider.id}
                </option>
              ))}
            </Select>
          </FormRow>
          <Button variant="outline" onClick={addProvider}>
            <Plus size={16} />
            {t("system.ai.providers.add")}
          </Button>
        </div>
      </Card>
      {draft.providers.map((provider) => {
        const codex = provider.protocol === "codexAppServer";
        const efforts =
          models.find(
            (model) =>
              model.providerId === provider.id && model.id === provider.model,
          )?.reasoningEfforts ?? [];
        const setModel = (
          index: number,
          change: Partial<AiProviderSettings["models"][number]>,
        ) =>
          updateProvider(provider.id, {
            models: provider.models.map((model, modelIndex) =>
              modelIndex === index ? { ...model, ...change } : model,
            ),
            reasoningEffort: null,
          });
        return (
          <Card
            key={provider.id}
            className="mt-5"
            icon={<ServerCog size={18} />}
            title={provider.name || t("system.ai.providers.title")}
          >
            <div className="mb-4 flex items-center justify-between gap-3">
              <span className="break-all text-xs text-subtle">
                {provider.id}
              </span>
              <Button
                variant="outline"
                size="sm"
                color="danger"
                onClick={() => removeProvider(provider.id)}
                aria-label={t("system.ai.providers.removeName", {
                  name: provider.name,
                })}
              >
                <Trash2 size={14} />
                {t("system.ai.providers.remove")}
              </Button>
            </div>
            <div className="grid gap-5 sm:grid-cols-2">
              <FormRow label={t("system.ai.providers.name")}>
                <Input
                  value={provider.name}
                  isInvalid={!provider.name.trim()}
                  onChange={(event) =>
                    updateProvider(provider.id, { name: event.target.value })
                  }
                />
              </FormRow>
              <FormRow label={t("system.ai.providers.protocol")}>
                <Select
                  value={provider.protocol}
                  onChange={(event) => {
                    const protocol = event.target.value as AiProtocol;
                    const sameOpenAiFamily =
                      protocol.startsWith("openAI") &&
                      provider.protocol.startsWith("openAI");
                    const defaults = sameOpenAiFamily
                      ? {}
                      : protocolDefaults(protocol);
                    updateProvider(provider.id, {
                      protocol,
                      ...defaults,
                      reasoningEffort:
                        chatCompletionsDefaultEffort(
                          protocol,
                          defaults.model ?? provider.model,
                        ) ??
                        (sameOpenAiFamily &&
                        !(
                          protocol === "openAIChatCompletions" &&
                          /^gpt-6-astra(?:-|$)/.test(provider.model)
                        )
                          ? provider.reasoningEffort
                          : null),
                      models: sameOpenAiFamily ? provider.models : [],
                      ...(protocol === "codexAppServer" &&
                      provider.protocol !== "codexAppServer"
                        ? {
                            apiKeyDraft: {
                              operation: "clear" as const,
                              value: "",
                            },
                          }
                        : provider.protocol === "codexAppServer" &&
                            protocol !== "codexAppServer"
                          ? {
                              tokenDraft: {
                                operation: "clear" as const,
                                value: "",
                              },
                            }
                          : {}),
                    });
                  }}
                >
                  <option value="openAIResponses">OpenAI Responses</option>
                  <option value="openAIChatCompletions">
                    OpenAI Chat Completions
                  </option>
                  <option value="anthropic">Anthropic Messages</option>
                  <option value="codexAppServer">Codex App Server</option>
                </Select>
                {provider.protocol === "openAIChatCompletions" && (
                  <p className="mt-1 text-xs leading-body text-subtle">
                    {t("system.ai.providers.chatCompletionsHelp")}
                  </p>
                )}
              </FormRow>
              <FormRow
                label={t(
                  codex
                    ? "system.ai.codex.endpoint"
                    : "system.ai.builtIn.baseUrl",
                )}
              >
                <Input
                  type="url"
                  value={codex ? provider.endpoint : provider.baseUrl}
                  isInvalid={
                    codex
                      ? !isCodexEndpoint(provider.endpoint)
                      : !isHttpEndpoint(provider.baseUrl)
                  }
                  onChange={(event) =>
                    updateProvider(
                      provider.id,
                      codex
                        ? { endpoint: event.target.value }
                        : { baseUrl: event.target.value },
                    )
                  }
                />
                {credentialRequired(provider) && (
                  <p className="mt-1 text-xs text-warning">
                    {t("system.ai.originCredentialRequired")}
                  </p>
                )}
                {codex && !isCodexEndpoint(provider.endpoint) && (
                  <p className="mt-1 text-xs text-error">
                    {t("system.ai.codex.endpointError")}
                  </p>
                )}
              </FormRow>
              <FormRow label={t("system.ai.builtIn.model")}>
                <Input
                  list={`models-${provider.id}`}
                  value={provider.model}
                  isInvalid={!codex && !provider.model.trim()}
                  placeholder={
                    codex ? t("system.ai.codex.modelPlaceholder") : undefined
                  }
                  onChange={(event) =>
                    updateProvider(provider.id, {
                      model: event.target.value,
                      reasoningEffort: chatCompletionsDefaultEffort(
                        provider.protocol,
                        event.target.value,
                      ),
                    })
                  }
                />
                <datalist id={`models-${provider.id}`}>
                  {models
                    .filter((model) => model.providerId === provider.id)
                    .map((model) => (
                      <option key={model.id} value={model.id}>
                        {model.name}
                      </option>
                    ))}
                </datalist>
              </FormRow>
              {codex ? (
                <>
                  <FormRow label={t("system.ai.codex.permissionProfile")}>
                    <Input
                      value={provider.permissionProfile}
                      onChange={(event) =>
                        updateProvider(provider.id, {
                          permissionProfile: event.target.value,
                        })
                      }
                    />
                    <p className="mt-1 text-xs text-subtle">
                      {t("system.ai.codex.permissionProfileHelp")}
                    </p>
                  </FormRow>
                  <FormRow label={t("system.ai.codex.timeout")}>
                    <Input
                      type="number"
                      min={1}
                      max={3600}
                      value={provider.timeoutSeconds}
                      onChange={(event) =>
                        updateProvider(provider.id, {
                          timeoutSeconds: Number(event.target.value),
                        })
                      }
                    />
                  </FormRow>
                  <SecretField
                    id={`token-${provider.id}`}
                    label={t("system.ai.codex.token")}
                    state={provider.token}
                    draft={provider.tokenDraft}
                    onChange={(tokenDraft) =>
                      updateProvider(provider.id, { tokenDraft })
                    }
                  />
                  {isRemoteWebSocketEndpoint(provider.endpoint) &&
                    !willSecretBeConfigured(
                      provider.token,
                      provider.tokenDraft,
                    ) && (
                      <p className="text-xs text-warning">
                        {t("system.ai.codex.remoteTokenRequired")}
                      </p>
                    )}
                </>
              ) : (
                <>
                  <FormRow label={t("system.ai.builtIn.maxTokens")}>
                    <Input
                      type="number"
                      min={1}
                      max={2_147_483_647}
                      value={provider.maxTokens}
                      isInvalid={
                        !isIntegerInRange(provider.maxTokens, 1, 2_147_483_647)
                      }
                      onChange={(event) =>
                        updateProvider(provider.id, {
                          maxTokens: Number(event.target.value),
                        })
                      }
                    />
                  </FormRow>
                  {provider.protocol === "anthropic" && (
                    <FormRow label={t("system.ai.builtIn.apiVersion")}>
                      <Input
                        value={provider.apiVersion}
                        onChange={(event) =>
                          updateProvider(provider.id, {
                            apiVersion: event.target.value,
                          })
                        }
                      />
                    </FormRow>
                  )}
                  <SecretField
                    id={`api-key-${provider.id}`}
                    label={t("system.ai.builtIn.apiKey")}
                    state={provider.apiKey}
                    draft={provider.apiKeyDraft}
                    onChange={(apiKeyDraft) =>
                      updateProvider(provider.id, { apiKeyDraft })
                    }
                  />
                </>
              )}
              {efforts.length > 0 && (
                <FormRow label={t("system.ai.providers.defaultEffort")}>
                  <Select
                    value={provider.reasoningEffort ?? ""}
                    onChange={(event) =>
                      updateProvider(provider.id, {
                        reasoningEffort: event.target.value || null,
                      })
                    }
                  >
                    <option value="">
                      {t("system.ai.providers.automatic")}
                    </option>
                    {efforts.map((effort) => (
                      <option key={effort} value={effort}>
                        {effort}
                      </option>
                    ))}
                  </Select>
                </FormRow>
              )}
            </div>
            {codex && (
              <p className="mt-4 text-xs leading-body text-muted">
                {t("system.ai.codex.serverAddressHelp")}
              </p>
            )}
            <div className="mt-5 border-t border-border-light pt-4">
              <h4 className="text-sm font-medium">
                {t("system.ai.providers.models")}
              </h4>
              <p className="mt-1 text-xs leading-body text-muted">
                {t("system.ai.providers.modelsHelp")}
              </p>
              {provider.models.map((model, index) => (
                <div
                  key={index}
                  className="mt-4 grid items-end gap-3 sm:grid-cols-[1fr_1fr_1.5fr_auto]"
                >
                  <FormRow label={t("system.ai.providers.modelId")}>
                    <Input
                      value={model.id}
                      onChange={(event) =>
                        setModel(index, { id: event.target.value })
                      }
                    />
                  </FormRow>
                  <FormRow label={t("system.ai.providers.modelName")}>
                    <Input
                      value={model.name}
                      onChange={(event) =>
                        setModel(index, { name: event.target.value })
                      }
                    />
                  </FormRow>
                  <FormRow label={t("system.ai.providers.efforts")}>
                    <Input
                      value={model.reasoningEfforts.join(",")}
                      placeholder="low,medium,high"
                      onChange={(event) =>
                        setModel(index, {
                          reasoningEfforts: event.target.value
                            .split(",")
                            .map((value) => value.trim()),
                        })
                      }
                    />
                  </FormRow>
                  <Button
                    variant="icon"
                    aria-label={t("system.ai.providers.removeModel", {
                      name: model.name || model.id,
                    })}
                    onClick={() =>
                      updateProvider(provider.id, {
                        models: provider.models.filter(
                          (_, modelIndex) => modelIndex !== index,
                        ),
                        reasoningEffort: null,
                      })
                    }
                  >
                    <Trash2 size={16} />
                  </Button>
                </div>
              ))}
              <Button
                className="mt-3"
                variant="outline"
                size="sm"
                onClick={() =>
                  updateProvider(provider.id, {
                    models: [
                      ...provider.models,
                      { id: "", name: "", reasoningEfforts: [] },
                    ],
                  })
                }
              >
                <Plus size={14} />
                {t("system.ai.providers.addModel")}
              </Button>
            </div>
          </Card>
        );
      })}
      <Card
        className="mt-5"
        title={t("system.ai.inference.title")}
        description={t("system.ai.providers.inferenceHelp")}
      >
        <ModelPicker
          models={models}
          defaultProviderId={draft.defaultProviderId ?? undefined}
          allowDefault
          selection={{
            providerId: draft.inference.providerId ?? undefined,
            model: draft.inference.model ?? undefined,
            reasoningEffort: draft.inference.reasoningEffort ?? undefined,
          }}
          onSelect={(selection) =>
            setDraft((current) => ({
              ...current,
              inference: {
                ...current.inference,
                providerId: selection.providerId ?? null,
                model: selection.model ?? null,
                reasoningEffort: selection.reasoningEffort ?? null,
              },
            }))
          }
        />
        <FormRow
          className="mt-5 max-w-sm"
          label={t("system.ai.inference.rateLimitDelay")}
        >
          <Input
            type="number"
            min={0}
            max={2_147_483_647}
            value={draft.inference.rateLimitDelayMs}
            onChange={(event) =>
              setDraft((current) => ({
                ...current,
                inference: {
                  ...current.inference,
                  rateLimitDelayMs: Number(event.target.value),
                },
              }))
            }
          />
        </FormRow>
      </Card>
      <SettingsSaveBar
        dirty={dirty}
        saving={saving}
        saved={saved}
        onReset={() => {
          setDraft(createDraft(value));
          setSaved(false);
        }}
        onSave={() => void save()}
      />
    </section>
  );
};
