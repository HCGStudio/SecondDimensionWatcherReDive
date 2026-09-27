import React from "react";
import { useTranslation } from "react-i18next";

import { AiModel, AiSelection } from "../../chat/types";
import "../../i18n/chatResources";
import { cn } from "../../lib/cn";
import { FormRow } from "../ui/FormRow";
import { Select, SelectItem } from "../ui/Select";

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
  const defaultModel = providerModels.find((model) => !model.id);

  return (
    <div className={cn("flex flex-wrap items-end gap-3", className)}>
      <FormRow label={t("selectProvider")} className="min-w-36 flex-1">
        <Select
          value={selection.providerId ?? ""}
          disabled={disabled}
          onValueChange={(value) => {
            const providerId = value || undefined;
            const first = models.find(
              (model) => model.providerId === providerId,
            );
            onSelect({
              providerId,
              model: allowDefault ? undefined : first?.id,
            });
          }}
        >
          <SelectItem value="" disabled={!allowDefault}>
            {t(allowDefault ? "configuredDefault" : "selectProvider")}
          </SelectItem>
          {providers.map(([id, name]) => (
            <SelectItem key={id} value={id}>
              {name} · {id}
            </SelectItem>
          ))}
        </Select>
      </FormRow>
      <FormRow label={t("selectModel")} className="min-w-44 flex-1">
        <Select
          value={selection.model ?? ""}
          disabled={disabled || !effectiveProviderId}
          onValueChange={(value) =>
            onSelect({
              ...selection,
              model: value || undefined,
              reasoningEffort: undefined,
            })
          }
        >
          <SelectItem value="">
            {allowDefault
              ? t("providerDefault")
              : (defaultModel?.name ?? t("providerDefault"))}
          </SelectItem>
          {selection.model && !selected && (
            <SelectItem value={selection.model}>{selection.model}</SelectItem>
          )}
          {providerModels
            .filter((model) => model.id)
            .map((model) => (
              <SelectItem key={model.id} value={model.id}>
                {model.name && model.name !== model.id
                  ? `${model.name} · ${model.id}`
                  : model.id}
              </SelectItem>
            ))}
        </Select>
      </FormRow>
      {efforts.length > 0 && (
        <FormRow label={t("reasoningEffort")} className="min-w-32">
          <Select
            value={selection.reasoningEffort ?? ""}
            disabled={disabled}
            onValueChange={(value) =>
              onSelect({
                ...selection,
                reasoningEffort: value || undefined,
              })
            }
          >
            <SelectItem value="">{t("configuredDefault")}</SelectItem>
            {efforts.map((effort) => (
              <SelectItem key={effort} value={effort}>
                {t(`efforts.${effort}`, { defaultValue: effort })}
              </SelectItem>
            ))}
          </Select>
        </FormRow>
      )}
    </div>
  );
};
