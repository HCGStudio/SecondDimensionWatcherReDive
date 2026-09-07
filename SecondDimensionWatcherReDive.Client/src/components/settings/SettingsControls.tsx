import React from "react";
import { useTranslation } from "react-i18next";

import { CheckCircle2, RotateCcw, Save, ShieldCheck } from "lucide-react";

import { cn } from "../../lib/cn";
import {
  SecretDraft,
  SecretOperation,
  SecretState,
} from "../../settings/systemTypes";
import { Button } from "../ui/Button";
import { FormRow } from "../ui/FormRow";
import { PasswordInput } from "../ui/PasswordInput";
import { Spinner } from "../ui/Spinner";

export const inputClassName = cn(
  "w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-foreground transition-colors",
  "focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus",
  "disabled:pointer-events-none disabled:opacity-50",
);

export interface SettingsSectionHeaderProps {
  title: string;
  description: string;
  eyebrow?: string;
}

export const SettingsSectionHeader: React.FC<SettingsSectionHeaderProps> = ({
  title,
  description,
  eyebrow,
}) => (
  <header className="mb-6">
    {eyebrow ? (
      <p className="mb-2 text-xs font-medium uppercase tracking-[0.12em] text-brand">
        {eyebrow}
      </p>
    ) : null}
    <h2 className="font-serif text-xl font-medium leading-heading text-foreground">
      {title}
    </h2>
    <p className="mt-2 max-w-3xl text-sm leading-body text-muted">
      {description}
    </p>
  </header>
);

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {}

export const Select: React.FC<SelectProps> = ({ className, ...props }) => (
  <select className={cn(inputClassName, className)} {...props} />
);

export interface ToggleFieldProps {
  checked: boolean;
  label: string;
  description?: string;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}

export const ToggleField: React.FC<ToggleFieldProps> = ({
  checked,
  label,
  description,
  disabled,
  onChange,
}) => (
  <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-border-light bg-canvas/50 px-4 py-3">
    <input
      type="checkbox"
      className="mt-0.5 h-4 w-4 shrink-0 accent-brand"
      checked={checked}
      disabled={disabled}
      onChange={(event) => onChange(event.target.checked)}
    />
    <span className="min-w-0">
      <span className="block text-sm font-medium text-foreground">{label}</span>
      {description ? (
        <span className="mt-0.5 block text-xs leading-body text-muted">
          {description}
        </span>
      ) : null}
    </span>
  </label>
);

export interface SecretFieldProps {
  id: string;
  label: string;
  state: SecretState;
  draft: SecretDraft;
  help?: string;
  disabled?: boolean;
  onChange: (draft: SecretDraft) => void;
}

export const SecretField: React.FC<SecretFieldProps> = ({
  id,
  label,
  state,
  draft,
  help,
  disabled,
  onChange,
}) => {
  const { t } = useTranslation("settings");
  const effectiveOperation: SecretOperation = draft.value
    ? "set"
    : draft.operation;
  return (
    <FormRow label={label} htmlFor={id}>
      <div className="space-y-2">
        <div className="relative">
          <PasswordInput
            id={id}
            value={draft.value}
            disabled={disabled || draft.operation !== "keep"}
            autoComplete="new-password"
            placeholder={
              state.isConfigured
                ? t("system.secret.configuredPlaceholder")
                : t("system.secret.emptyPlaceholder")
            }
            onChange={(event) =>
              onChange({ operation: "keep", value: event.target.value })
            }
          />
          {state.isConfigured && !draft.value && draft.operation === "keep" ? (
            <span className="pointer-events-none absolute right-10 top-1/2 inline-flex -translate-y-1/2 items-center gap-1 text-xs text-success">
              <ShieldCheck size={13} />
              {t("system.secret.configured")}
            </span>
          ) : null}
        </div>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <p className="max-w-xl text-xs leading-body text-subtle">
            {help ?? t("system.secret.keepHelp")}
          </p>
          <div className="flex items-center gap-2">
            <span className="text-xs text-subtle">
              {t(`system.secret.source.${state.source}`)}
            </span>
            <select
              aria-label={t("system.secret.operationLabel", { label })}
              className="rounded-md border border-border bg-surface px-2 py-1 text-xs text-foreground focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus"
              value={effectiveOperation}
              disabled={disabled}
              onChange={(event) => {
                const operation = event.target.value as SecretOperation;
                if (operation === "set") return;
                onChange({ operation, value: "" });
              }}
            >
              <option value="keep">{t("system.secret.keep")}</option>
              {draft.value ? (
                <option value="set">{t("system.secret.set")}</option>
              ) : null}
              <option value="clear">{t("system.secret.clear")}</option>
              <option value="reset" disabled={state.source !== "runtime"}>
                {t("system.secret.reset")}
              </option>
            </select>
          </div>
        </div>
      </div>
    </FormRow>
  );
};

export interface SettingsSaveBarProps {
  dirty: boolean;
  saving: boolean;
  saved?: boolean;
  requiresRestart?: boolean;
  onSave: () => void;
  onReset: () => void;
}

export const SettingsSaveBar: React.FC<SettingsSaveBarProps> = ({
  dirty,
  saving,
  saved,
  requiresRestart,
  onSave,
  onReset,
}) => {
  const { t } = useTranslation("settings");
  return (
    <div className="sticky bottom-2 z-10 mt-6 flex flex-col gap-3 rounded-lg border border-border bg-surface/95 px-4 py-3 shadow-whisper backdrop-blur sm:bottom-4 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
      <div
        className="min-w-0 text-sm"
        role="status"
        aria-live="polite"
        aria-atomic="true"
      >
        {requiresRestart ? (
          <span className="text-warning">
            {t("system.save.requiresRestart")}
          </span>
        ) : saved ? (
          <span className="inline-flex items-center gap-1.5 text-success">
            <CheckCircle2 size={15} />
            {t("system.save.saved")}
          </span>
        ) : dirty ? (
          <span className="text-muted">{t("system.save.unsaved")}</span>
        ) : (
          <span className="text-subtle">{t("system.save.noChanges")}</span>
        )}
      </div>
      <div className="flex items-center gap-2 sm:justify-end">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!dirty || saving}
          onClick={onReset}
          className="flex-1 sm:flex-none"
        >
          <RotateCcw size={14} />
          {t("system.save.reset")}
        </Button>
        <Button
          type="button"
          size="sm"
          disabled={!dirty || saving}
          onClick={onSave}
          className="flex-1 sm:flex-none"
        >
          {saving ? <Spinner className="h-4 w-4" /> : <Save size={14} />}
          {t("system.save.submit")}
        </Button>
      </div>
    </div>
  );
};

export const RestartNotice: React.FC<{ children?: React.ReactNode }> = ({
  children,
}) => {
  const { t } = useTranslation("settings");
  return (
    <div className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm leading-body text-muted">
      {children ?? t("system.restartNotice")}
    </div>
  );
};
