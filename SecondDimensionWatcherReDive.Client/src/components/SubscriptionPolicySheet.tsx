import type { TFunction } from "i18next";
import React from "react";
import { useTranslation } from "react-i18next";

import {
  Bell,
  Check,
  CircleAlert,
  CircleCheck,
  CircleX,
  Download,
  FlaskConical,
  MousePointerClick,
  Save,
  Trash2,
  X,
} from "lucide-react";

import { apiErrorStatus } from "../errors/apiError";
import { IFeed } from "../feed/IFeed";
import { cn } from "../lib/cn";
import {
  deleteSubscriptionPolicy,
  getSubscriptionPolicy,
  saveSubscriptionPolicy,
  simulateSubscriptionPolicy,
} from "../subscriptionPolicy/api";
import {
  ISubscriptionPolicy,
  ISubscriptionPolicyDraft,
  ISubscriptionPolicyExplanation,
  ISubscriptionPolicySimulation,
  SubscriptionPolicyMode,
  createEmptySubscriptionPolicy,
  toSubscriptionPolicyDraft,
} from "../subscriptionPolicy/types";
import { formatFileSize } from "../utils/formatBytes";
import { useToast } from "./ToastProvider";
import { Button } from "./ui/Button";
import { Input } from "./ui/Input";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "./ui/Sheet";
import { Spinner } from "./ui/Spinner";

const MB = 1024 * 1024;

const MODE_OPTIONS = [
  { value: "NotifyOnly" as const, icon: Bell },
  { value: "ManualConfirm" as const, icon: MousePointerClick },
  { value: "AutoDownload" as const, icon: Download },
];

const FIELD_SUGGESTIONS = {
  subtitleGroups: ["LoliHouse", "喵萌奶茶屋", "ANi", "SubsPlease"],
  resolutions: ["2160p", "1080p", "720p"],
  codecs: ["AV1", "HEVC", "AVC"],
  languages: ["简中", "繁中", "日语"],
};

export interface SubscriptionPolicySheetProps {
  feed: IFeed | null;
  initialPolicy?: ISubscriptionPolicy;
  onOpenChange: (open: boolean) => void;
  onPolicyChanged: () => Promise<unknown> | unknown;
  restoreFocusRef: React.RefObject<HTMLButtonElement | null>;
}

export const SubscriptionPolicySheet: React.FC<
  SubscriptionPolicySheetProps
> = ({
  feed,
  initialPolicy,
  onOpenChange,
  onPolicyChanged,
  restoreFocusRef,
}) => {
  const { t } = useTranslation("feeds");
  const { addToast } = useToast();
  const [draft, setDraft] = React.useState<ISubscriptionPolicyDraft>(
    createEmptySubscriptionPolicy,
  );
  const [hasSavedPolicy, setHasSavedPolicy] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [saving, setSaving] = React.useState(false);
  const [simulating, setSimulating] = React.useState(false);
  const [simulation, setSimulation] =
    React.useState<ISubscriptionPolicySimulation | null>(null);

  React.useEffect(() => {
    if (!feed) return;

    let isCurrent = true;
    const matchingInitial =
      initialPolicy?.feedId === feed.id ? initialPolicy : undefined;

    setDraft(
      matchingInitial
        ? toSubscriptionPolicyDraft(matchingInitial)
        : createEmptySubscriptionPolicy(),
    );
    setHasSavedPolicy(!!matchingInitial);
    setSimulation(null);
    setLoading(true);

    getSubscriptionPolicy(feed.id)
      .then((policy) => {
        if (!isCurrent) return;
        setDraft(toSubscriptionPolicyDraft(policy));
        setHasSavedPolicy(true);
      })
      .catch((error: unknown) => {
        if (!isCurrent) return;
        if (apiErrorStatus(error) === 404) {
          setDraft(createEmptySubscriptionPolicy());
          setHasSavedPolicy(false);
          return;
        }
        addToast({
          title: t("automation.toast.loadFailed"),
          color: "danger",
        });
      })
      .finally(() => {
        if (isCurrent) setLoading(false);
      });

    return () => {
      isCurrent = false;
    };
  }, [feed, initialPolicy, addToast, t]);

  const sizeRangeIsInvalid =
    draft.minSizeBytes != null &&
    draft.maxSizeBytes != null &&
    draft.minSizeBytes > draft.maxSizeBytes;

  const updateDraft = React.useCallback(
    (update: React.SetStateAction<ISubscriptionPolicyDraft>) => {
      setDraft(update);
      setSimulation(null);
    },
    [],
  );

  const updateArray = React.useCallback(
    (
      field:
        | "subtitleGroups"
        | "resolutions"
        | "codecs"
        | "languages"
        | "excludedKeywords",
      values: string[],
    ) => updateDraft((current) => ({ ...current, [field]: values })),
    [updateDraft],
  );

  const handleSave = React.useCallback(async () => {
    if (!feed || saving || sizeRangeIsInvalid) return;
    setSaving(true);
    try {
      const saved = await saveSubscriptionPolicy(feed.id, draft);
      setDraft(toSubscriptionPolicyDraft(saved));
      setHasSavedPolicy(true);
      await onPolicyChanged();
      addToast({
        title: t("automation.toast.saved", { name: feed.name || feed.url }),
        color: "success",
      });
    } catch {
      addToast({
        title: t("automation.toast.saveFailed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  }, [feed, saving, sizeRangeIsInvalid, draft, onPolicyChanged, addToast, t]);

  const handleDelete = React.useCallback(async () => {
    if (!feed || !hasSavedPolicy) return;
    if (!window.confirm(t("automation.deleteConfirm"))) return;

    setSaving(true);
    try {
      await deleteSubscriptionPolicy(feed.id);
      setDraft(createEmptySubscriptionPolicy());
      setHasSavedPolicy(false);
      setSimulation(null);
      await onPolicyChanged();
      addToast({
        title: t("automation.toast.deleted"),
        color: "success",
      });
    } catch {
      addToast({
        title: t("automation.toast.deleteFailed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  }, [feed, hasSavedPolicy, onPolicyChanged, addToast, t]);

  const handleSimulate = React.useCallback(async () => {
    if (!feed || simulating || sizeRangeIsInvalid) return;
    setSimulating(true);
    try {
      const result = await simulateSubscriptionPolicy(feed.id, draft);
      setSimulation(result);
    } catch {
      addToast({
        title: t("automation.toast.simulateFailed"),
        color: "danger",
      });
    } finally {
      setSimulating(false);
    }
  }, [feed, simulating, sizeRangeIsInvalid, draft, addToast, t]);

  return (
    <Sheet open={feed != null} onOpenChange={onOpenChange}>
      <SheetContent
        className="max-w-3xl"
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          restoreFocusRef.current?.focus();
        }}
      >
        <SheetHeader>
          <SheetTitle>{t("automation.editor.title")}</SheetTitle>
          <p className="mt-1 truncate pr-8 text-sm text-muted">
            {feed?.name || feed?.url || ""}
          </p>
        </SheetHeader>
        <SheetBody className="px-0 py-0">
          {loading ? (
            <div className="flex h-64 items-center justify-center">
              <Spinner />
            </div>
          ) : (
            <div className="divide-y divide-border-light">
              <section className="px-4 py-5 sm:px-6">
                <SectionHeading
                  number="01"
                  title={t("automation.mode.title")}
                  description={t("automation.mode.description")}
                />
                <fieldset className="mt-4 grid gap-3 sm:grid-cols-3">
                  <legend className="sr-only">
                    {t("automation.mode.title")}
                  </legend>
                  {MODE_OPTIONS.map((option) => {
                    const selected = draft.mode === option.value;
                    const Icon = option.icon;
                    return (
                      <label
                        key={option.value}
                        className={cn(
                          "relative cursor-pointer rounded-lg border p-4 transition-colors focus-within:ring-2 focus-within:ring-focus",
                          selected
                            ? "border-brand bg-brand/5 shadow-ring-brand"
                            : "border-border bg-surface hover:border-ring-deep",
                        )}
                      >
                        <input
                          className="sr-only"
                          type="radio"
                          name="subscription-policy-mode"
                          value={option.value}
                          checked={selected}
                          onChange={() =>
                            updateDraft((current) => ({
                              ...current,
                              mode: option.value,
                            }))
                          }
                        />
                        <div className="flex items-center justify-between gap-2">
                          <Icon
                            size={18}
                            className={selected ? "text-brand" : "text-muted"}
                          />
                          {selected ? (
                            <span className="flex h-5 w-5 items-center justify-center rounded-full bg-brand text-surface">
                              <Check size={12} strokeWidth={3} />
                            </span>
                          ) : null}
                        </div>
                        <p className="mt-3 text-sm font-medium text-foreground">
                          {t(`automation.mode.options.${option.value}.title`)}
                        </p>
                        <p className="mt-1 text-xs leading-body text-muted">
                          {t(
                            `automation.mode.options.${option.value}.description`,
                          )}
                        </p>
                      </label>
                    );
                  })}
                </fieldset>
              </section>

              <section className="px-4 py-5 sm:px-6">
                <SectionHeading
                  number="02"
                  title={t("automation.filters.title")}
                  description={t("automation.filters.description")}
                />
                <div className="mt-5 grid gap-5 sm:grid-cols-2">
                  <TagField
                    id="policy-subtitle-groups"
                    label={t("automation.filters.subtitleGroups.label")}
                    help={t("automation.filters.subtitleGroups.help")}
                    placeholder={t(
                      "automation.filters.subtitleGroups.placeholder",
                    )}
                    values={draft.subtitleGroups}
                    suggestions={FIELD_SUGGESTIONS.subtitleGroups}
                    onChange={(values) => updateArray("subtitleGroups", values)}
                  />
                  <TagField
                    id="policy-resolutions"
                    label={t("automation.filters.resolutions.label")}
                    help={t("automation.filters.resolutions.help")}
                    placeholder={t(
                      "automation.filters.resolutions.placeholder",
                    )}
                    values={draft.resolutions}
                    suggestions={FIELD_SUGGESTIONS.resolutions}
                    onChange={(values) => updateArray("resolutions", values)}
                  />
                  <TagField
                    id="policy-codecs"
                    label={t("automation.filters.codecs.label")}
                    help={t("automation.filters.codecs.help")}
                    placeholder={t("automation.filters.codecs.placeholder")}
                    values={draft.codecs}
                    suggestions={FIELD_SUGGESTIONS.codecs}
                    onChange={(values) => updateArray("codecs", values)}
                  />
                  <TagField
                    id="policy-languages"
                    label={t("automation.filters.languages.label")}
                    help={t("automation.filters.languages.help")}
                    placeholder={t("automation.filters.languages.placeholder")}
                    values={draft.languages}
                    suggestions={FIELD_SUGGESTIONS.languages}
                    onChange={(values) => updateArray("languages", values)}
                  />
                </div>

                <div className="mt-5 rounded-lg border border-border-light bg-canvas/60 p-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <SizeInput
                      id="policy-min-size"
                      label={t("automation.filters.minSize.label")}
                      placeholder={t("automation.filters.minSize.placeholder")}
                      value={draft.minSizeBytes}
                      onChange={(value) =>
                        updateDraft((current) => ({
                          ...current,
                          minSizeBytes: value,
                        }))
                      }
                    />
                    <SizeInput
                      id="policy-max-size"
                      label={t("automation.filters.maxSize.label")}
                      placeholder={t("automation.filters.maxSize.placeholder")}
                      value={draft.maxSizeBytes}
                      onChange={(value) =>
                        updateDraft((current) => ({
                          ...current,
                          maxSizeBytes: value,
                        }))
                      }
                    />
                  </div>
                  {sizeRangeIsInvalid ? (
                    <p
                      role="status"
                      aria-live="polite"
                      className="mt-2 flex items-center gap-1.5 text-xs text-error"
                    >
                      <CircleAlert size={13} />
                      {t("automation.filters.sizeRangeError")}
                    </p>
                  ) : (
                    <p className="mt-2 text-xs text-subtle">
                      {t("automation.filters.sizeHelp")}
                    </p>
                  )}
                </div>

                <div className="mt-5">
                  <TagField
                    id="policy-excluded-keywords"
                    label={t("automation.filters.excludedKeywords.label")}
                    help={t("automation.filters.excludedKeywords.help")}
                    placeholder={t(
                      "automation.filters.excludedKeywords.placeholder",
                    )}
                    values={draft.excludedKeywords}
                    onChange={(values) =>
                      updateArray("excludedKeywords", values)
                    }
                  />
                </div>
              </section>

              <section className="px-4 py-5 sm:px-6">
                <span
                  className="sr-only"
                  role="status"
                  aria-live="polite"
                  aria-atomic="true"
                >
                  {simulating
                    ? t("automation.simulation.running")
                    : simulation
                      ? t("automation.simulation.summary", {
                          matched: simulation.matched,
                          total: simulation.total,
                        })
                      : ""}
                </span>
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <SectionHeading
                    number="03"
                    title={t("automation.simulation.title")}
                    description={t("automation.simulation.description")}
                  />
                  <Button
                    variant="outline"
                    onClick={handleSimulate}
                    disabled={simulating || sizeRangeIsInvalid}
                  >
                    {simulating ? (
                      <Spinner className="h-4 w-4" />
                    ) : (
                      <FlaskConical size={16} />
                    )}
                    {t("automation.simulation.run")}
                  </Button>
                </div>
                {simulation ? (
                  <SimulationResults result={simulation} />
                ) : (
                  <div className="mt-4 rounded-lg border border-dashed border-border bg-canvas/50 px-5 py-8 text-center">
                    <FlaskConical
                      size={24}
                      className="mx-auto text-warm-silver"
                    />
                    <p className="mt-2 text-sm text-muted">
                      {t("automation.simulation.empty")}
                    </p>
                  </div>
                )}
              </section>
            </div>
          )}
        </SheetBody>
        <div className="flex flex-col gap-3 border-t border-border bg-surface px-4 py-4 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between sm:px-6">
          <div>
            {hasSavedPolicy ? (
              <Button
                variant="ghost"
                color="danger"
                onClick={handleDelete}
                disabled={saving || loading}
              >
                <Trash2 size={15} />
                {t("automation.actions.remove")}
              </Button>
            ) : (
              <span className="text-xs text-subtle">
                {t("automation.actions.unsaved")}
              </span>
            )}
          </div>
          <div className="flex w-full gap-2 sm:w-auto">
            <Button
              variant="outline"
              className="flex-1 sm:flex-none"
              onClick={() => onOpenChange(false)}
            >
              {t("automation.actions.cancel")}
            </Button>
            <Button
              className="flex-1 sm:flex-none"
              onClick={handleSave}
              disabled={saving || loading || sizeRangeIsInvalid}
            >
              {saving ? <Spinner className="h-4 w-4" /> : <Save size={16} />}
              {t("automation.actions.save")}
            </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
};

interface SectionHeadingProps {
  number: string;
  title: string;
  description: string;
}

const SectionHeading: React.FC<SectionHeadingProps> = ({
  number,
  title,
  description,
}) => (
  <div className="flex items-start gap-3">
    <span className="mt-0.5 font-mono text-xs font-medium text-brand">
      {number}
    </span>
    <div>
      <h3 className="font-serif text-lg font-medium text-foreground">
        {title}
      </h3>
      <p className="mt-1 max-w-xl text-sm leading-body text-muted">
        {description}
      </p>
    </div>
  </div>
);

interface TagFieldProps {
  id: string;
  label: string;
  help: string;
  placeholder: string;
  values: string[];
  suggestions?: string[];
  onChange: (values: string[]) => void;
}

const TagField: React.FC<TagFieldProps> = ({
  id,
  label,
  help,
  placeholder,
  values,
  suggestions,
  onChange,
}) => {
  const { t } = useTranslation("feeds");
  const [input, setInput] = React.useState("");

  const addValues = React.useCallback(
    (raw: string) => {
      const additions = raw
        .split(/[,，\n]/)
        .map((value) => value.trim())
        .filter(Boolean);
      if (additions.length === 0) return;

      const next = [...values];
      for (const addition of additions) {
        if (
          !next.some((value) => value.toLowerCase() === addition.toLowerCase())
        ) {
          next.push(addition);
        }
      }
      onChange(next);
      setInput("");
    },
    [values, onChange],
  );

  return (
    <div>
      <label htmlFor={id} className="text-sm font-medium text-foreground">
        {label}
      </label>
      <p id={`${id}-help`} className="mt-0.5 text-xs leading-body text-subtle">
        {help}
      </p>
      <div className="mt-2 flex min-h-10 flex-wrap items-center gap-1.5 rounded-lg border border-border bg-surface p-1.5 focus-within:border-focus focus-within:ring-2 focus-within:ring-focus">
        {values.map((value) => (
          <span
            key={value}
            className="inline-flex items-center gap-1 rounded-md bg-canvas px-2 py-1 text-xs text-foreground shadow-ring"
          >
            {value}
            <button
              type="button"
              className="relative rounded text-subtle transition-colors before:absolute before:-inset-1.5 hover:text-error focus:outline-hidden focus:ring-2 focus:ring-focus"
              aria-label={t("automation.filters.removeValue", { value })}
              onClick={() => onChange(values.filter((item) => item !== value))}
            >
              <X size={12} />
            </button>
          </span>
        ))}
        <input
          id={id}
          aria-describedby={`${id}-help`}
          className="min-w-28 flex-1 bg-transparent px-1.5 py-1 text-sm text-foreground outline-hidden placeholder:text-subtle"
          value={input}
          placeholder={values.length === 0 ? placeholder : ""}
          onChange={(event) => setInput(event.target.value)}
          onKeyDown={(event) => {
            if (event.nativeEvent.isComposing) return;
            if (event.key === "Enter" || event.key === ",") {
              event.preventDefault();
              addValues(input);
            }
            if (event.key === "Backspace" && !input && values.length > 0) {
              onChange(values.slice(0, -1));
            }
          }}
          onBlur={() => addValues(input)}
          onPaste={(event) => {
            const pasted = event.clipboardData.getData("text");
            if (/[,，\n]/.test(pasted)) {
              event.preventDefault();
              addValues(pasted);
            }
          }}
        />
      </div>
      {suggestions ? (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {suggestions
            .filter(
              (suggestion) =>
                !values.some(
                  (value) => value.toLowerCase() === suggestion.toLowerCase(),
                ),
            )
            .map((suggestion) => (
              <button
                type="button"
                key={suggestion}
                className="rounded-md border border-border-light px-2 py-1 text-xs text-muted transition-colors hover:border-border hover:bg-canvas hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
                onClick={() => addValues(suggestion)}
              >
                + {suggestion}
              </button>
            ))}
        </div>
      ) : null}
    </div>
  );
};

interface SizeInputProps {
  id: string;
  label: string;
  placeholder: string;
  value: number | null;
  onChange: (value: number | null) => void;
}

const SizeInput: React.FC<SizeInputProps> = ({
  id,
  label,
  placeholder,
  value,
  onChange,
}) => (
  <div>
    <label htmlFor={id} className="text-sm font-medium text-foreground">
      {label}
    </label>
    <div className="relative mt-1.5">
      <Input
        id={id}
        type="number"
        inputMode="decimal"
        min="0"
        step="50"
        className="pr-12"
        placeholder={placeholder}
        value={value == null ? "" : Number((value / MB).toFixed(2))}
        onChange={(event) => {
          const raw = event.target.value;
          if (raw === "") {
            onChange(null);
            return;
          }
          const parsed = Number(raw);
          if (Number.isFinite(parsed) && parsed >= 0) {
            onChange(Math.round(parsed * MB));
          }
        }}
      />
      <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-subtle">
        MB
      </span>
    </div>
  </div>
);

const SimulationResults: React.FC<{
  result: ISubscriptionPolicySimulation;
}> = ({ result }) => {
  const { t } = useTranslation("feeds");
  const unmatched = result.total - result.matched;

  return (
    <div className="mt-4">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-lg border border-border bg-canvas/60 px-4 py-3">
        <p className="text-sm font-medium text-foreground">
          {t("automation.simulation.summary", {
            matched: result.matched,
            total: result.total,
          })}
        </p>
        <span className="flex items-center gap-1.5 text-xs text-success">
          <CircleCheck size={14} />
          {t("automation.simulation.matchedCount", { count: result.matched })}
        </span>
        <span className="flex items-center gap-1.5 text-xs text-error">
          <CircleX size={14} />
          {t("automation.simulation.unmatchedCount", { count: unmatched })}
        </span>
      </div>

      {result.entries.length === 0 ? (
        <p className="py-8 text-center text-sm text-muted">
          {t("automation.simulation.noHistory")}
        </p>
      ) : (
        <div className="mt-3 space-y-3">
          {result.entries.map((entry) => (
            <article
              key={entry.id}
              className={cn(
                "overflow-hidden rounded-lg border bg-surface",
                entry.matched ? "border-success/30" : "border-border",
              )}
            >
              <div className="flex items-start gap-3 px-4 py-3">
                {entry.matched ? (
                  <CircleCheck
                    size={18}
                    className="mt-0.5 shrink-0 text-success"
                  />
                ) : (
                  <CircleX size={18} className="mt-0.5 shrink-0 text-error" />
                )}
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <h4 className="break-words text-sm font-medium leading-snug text-foreground">
                      {entry.title}
                    </h4>
                    <span
                      className={cn(
                        "shrink-0 rounded-full px-2 py-0.5 text-xs font-medium",
                        entry.matched
                          ? "bg-success/10 text-success"
                          : "bg-error/10 text-error",
                      )}
                    >
                      {t(
                        entry.matched
                          ? "automation.simulation.matched"
                          : "automation.simulation.unmatched",
                      )}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-subtle">
                    {new Date(entry.publishedAt).toLocaleString()}
                    {entry.sizeBytes != null
                      ? ` · ${formatFileSize(entry.sizeBytes)}`
                      : ""}
                  </p>
                </div>
              </div>
              <div className="border-t border-border-light bg-canvas/40 px-4 py-3">
                <p className="mb-2 text-xs font-medium uppercase tracking-wide text-subtle">
                  {t("automation.simulation.explanationTitle")}
                </p>
                <ul className="space-y-2">
                  {entry.explanations.map((explanation, index) => (
                    <li
                      key={`${explanation.field}-${index}`}
                      className="flex items-start gap-2 text-xs leading-body"
                    >
                      {explanation.passed ? (
                        <Check
                          size={13}
                          className="mt-0.5 shrink-0 text-success"
                        />
                      ) : (
                        <X size={13} className="mt-0.5 shrink-0 text-error" />
                      )}
                      <div className="min-w-0">
                        <span className="font-medium text-foreground">
                          {t(
                            `automation.simulation.fields.${explanation.field}`,
                            { defaultValue: explanation.field },
                          )}
                        </span>
                        <span className="text-muted">
                          {" — "}
                          {getExplanationReason(explanation, t)}
                        </span>
                        {explanation.actual != null ||
                        explanation.expected != null ? (
                          <p className="mt-0.5 break-words font-mono text-[11px] text-subtle">
                            {explanation.actual != null
                              ? t("automation.simulation.actual", {
                                  value: explanation.actual,
                                })
                              : ""}
                            {explanation.actual != null &&
                            explanation.expected != null
                              ? " · "
                              : ""}
                            {explanation.expected != null
                              ? t("automation.simulation.expected", {
                                  value: explanation.expected,
                                })
                              : ""}
                          </p>
                        ) : null}
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  );
};

export const SubscriptionPolicyModeBadge: React.FC<{
  mode: SubscriptionPolicyMode;
}> = ({ mode }) => {
  const { t } = useTranslation("feeds");
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        mode === "AutoDownload"
          ? "bg-success/10 text-success"
          : mode === "ManualConfirm"
            ? "bg-warning/10 text-warning"
            : "bg-brand/10 text-brand",
      )}
    >
      {t(`automation.mode.options.${mode}.title`)}
    </span>
  );
};

const MOCK_REASON_CODES = new Set([
  "anyValueAllowed",
  "allowedValueMatched",
  "allowedValueMissed",
  "withinSizeRange",
  "outsideSizeRange",
  "noExcludedKeyword",
  "excludedKeywordFound",
]);

const getExplanationReason = (
  explanation: ISubscriptionPolicyExplanation,
  t: TFunction<"feeds">,
) => {
  if (MOCK_REASON_CODES.has(explanation.message)) {
    return t(`automation.simulation.reasons.${explanation.message}`);
  }
  if (explanation.expected == null) {
    return t("automation.simulation.reasons.anyValueAllowed");
  }
  if (explanation.actual == null) {
    return t("automation.simulation.reasons.valueUnavailable");
  }
  if (explanation.field === "excludedKeywords") {
    return t(
      explanation.passed
        ? "automation.simulation.reasons.noExcludedKeyword"
        : "automation.simulation.reasons.excludedKeywordFound",
    );
  }
  if (explanation.field === "size") {
    return t(
      explanation.passed
        ? "automation.simulation.reasons.withinSizeRange"
        : "automation.simulation.reasons.outsideSizeRange",
    );
  }
  return t(
    explanation.passed
      ? "automation.simulation.reasons.allowedValueMatched"
      : "automation.simulation.reasons.allowedValueMissed",
  );
};
