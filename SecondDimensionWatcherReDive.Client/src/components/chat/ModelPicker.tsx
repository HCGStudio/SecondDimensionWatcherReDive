import React from "react";
import { useTranslation } from "react-i18next";

import { AiModel, AiSelection } from "../../chat/types";
import "../../i18n/chatResources";
import { cn } from "../../lib/cn";
import { Select } from "../settings/SettingsControls";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";

interface ModelPickerProps {
  models: AiModel[];
  selection: AiSelection;
  onSelect: (selection: AiSelection) => void;
  allowDefault?: boolean;
  defaultProviderId?: string;
  disabled?: boolean;
  className?: string;
}

export const ModelPicker: React.FC<ModelPickerProps> = ({
  models,
  selection,
  onSelect,
  allowDefault = false,
  defaultProviderId,
  disabled = false,
  className,
}) => {
  const { t } = useTranslation("chat");
  const listId = React.useId();
  const providers = [
    ...new Map(
      models.map((model) => [model.providerId, model.provider]),
    ).entries(),
  ];
  const effectiveProviderId = selection.providerId ?? defaultProviderId;
  const providerModels = models.filter(
    (model) => model.providerId === effectiveProviderId,
  );
  const selected = selection.model
    ? providerModels.find((model) => model.id === selection.model)
    : providerModels[0];
  const efforts = selected?.reasoningEfforts ?? [];

  return (
    <div className={cn("flex flex-wrap items-end gap-3", className)}>
      <FormRow label={t("selectProvider")} className="min-w-36 flex-1">
        <Select
          value={selection.providerId ?? ""}
          disabled={disabled}
          onChange={(event) => {
            const providerId = event.target.value || undefined;
            const first = models.find(
              (model) => model.providerId === providerId,
            );
            onSelect({
              providerId,
              model: allowDefault ? undefined : first?.id,
            });
          }}
        >
          <option value="" disabled={!allowDefault}>
            {t(allowDefault ? "configuredDefault" : "selectProvider")}
          </option>
          {providers.map(([id, name]) => (
            <option key={id} value={id}>
              {name} · {id}
            </option>
          ))}
        </Select>
      </FormRow>
      <FormRow label={t("selectModel")} className="min-w-44 flex-1">
        <Input
          list={listId}
          value={selection.model ?? ""}
          disabled={disabled || !effectiveProviderId}
          placeholder={t(allowDefault ? "providerDefault" : "customModel")}
          onChange={(event) =>
            onSelect({
              ...selection,
              model: event.target.value || undefined,
              reasoningEffort: undefined,
            })
          }
        />
        <datalist id={listId}>
          {providerModels.map((model) => (
            <option key={model.id} value={model.id}>
              {model.name}
            </option>
          ))}
        </datalist>
      </FormRow>
      {efforts.length > 0 && (
        <FormRow label={t("reasoningEffort")} className="min-w-32">
          <Select
            value={selection.reasoningEffort ?? ""}
            disabled={disabled}
            onChange={(event) =>
              onSelect({
                ...selection,
                reasoningEffort: event.target.value || undefined,
              })
            }
          >
            <option value="">{t("configuredDefault")}</option>
            {efforts.map((effort) => (
              <option key={effort} value={effort}>
                {t(`efforts.${effort}`, { defaultValue: effort })}
              </option>
            ))}
          </Select>
        </FormRow>
      )}
    </div>
  );
};
