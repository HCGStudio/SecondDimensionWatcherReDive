import React from "react";
import { useTranslation } from "react-i18next";

import {
  AlertCircle,
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  FileText,
} from "lucide-react";

import {
  applyMetadataReview,
  metadataReviewErrorStatus,
  previewMetadataReview,
} from "../metadataReview/api";
import {
  EditableMetadata,
  MetadataPathChange,
  MetadataRemapResult,
  MetadataReviewItem,
  MetadataReviewPreview,
} from "../metadataReview/types";
import { Button } from "./ui/Button";
import { FormRow } from "./ui/FormRow";
import { Input } from "./ui/Input";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "./ui/Sheet";
import { Spinner } from "./ui/Spinner";

interface MetadataReviewSheetProps {
  item: MetadataReviewItem | null;
  onOpenChange: (open: boolean) => void;
  onApplied: (result: MetadataRemapResult) => void | Promise<void>;
  restoreFocusRef: React.RefObject<HTMLButtonElement | null>;
}

interface EditorFormValues {
  tmdbId: string;
  season: string;
  episode: string;
  groupName: string;
}

type EditorField = keyof EditorFormValues;
type FieldErrors = Partial<Record<EditorField, string>>;

const emptyForm: EditorFormValues = {
  tmdbId: "",
  season: "",
  episode: "",
  groupName: "",
};

function formFromItem(item: MetadataReviewItem): EditorFormValues {
  return {
    tmdbId: item.metadata.tmdbId ?? "",
    season: item.metadata.season == null ? "" : String(item.metadata.season),
    episode: item.metadata.episode == null ? "" : String(item.metadata.episode),
    groupName: item.metadata.groupName ?? "",
  };
}

function apiErrorKey(error: unknown): string {
  const status = metadataReviewErrorStatus(error);
  if (status === 409) return "editor.errors.conflict";
  if (status === 422) return "editor.errors.validation";
  return "editor.errors.requestFailed";
}

interface PathDiffProps {
  changes: MetadataPathChange[];
}

const PathDiff: React.FC<PathDiffProps> = ({ changes }) => {
  const { t } = useTranslation("metadataReview");

  if (changes.length === 0) {
    return (
      <div className="rounded-lg border border-dashed border-border px-4 py-5 text-center text-sm text-muted">
        {t("preview.noPathChanges")}
      </div>
    );
  }

  return (
    <ul className="divide-y divide-border-light overflow-hidden rounded-lg border border-border bg-canvas/50">
      {changes.map((change, index) => (
        <li key={`${change.fileName}-${index}`} className="p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="min-w-0 truncate text-sm font-medium text-foreground">
              {change.fileName}
            </span>
            <span className="rounded-full bg-surface px-2 py-0.5 text-xs text-muted shadow-ring">
              {t(`preview.changeKinds.${change.changeKind}`)}
            </span>
          </div>
          <div className="mt-2 grid gap-1 font-mono text-xs leading-relaxed sm:grid-cols-[1fr_auto_1fr] sm:items-center">
            <span className="break-all text-muted">
              {change.currentVirtualPath ?? t("preview.noPath")}
            </span>
            <ArrowRight size={14} className="hidden text-subtle sm:block" />
            <span className="break-all text-foreground">
              {change.proposedVirtualPath ?? t("preview.noPath")}
            </span>
          </div>
          {change.collisionAdjusted ? (
            <p className="mt-2 text-xs text-warning">
              {t("preview.collisionAdjusted")}
            </p>
          ) : null}
        </li>
      ))}
    </ul>
  );
};

interface PreviewPanelProps {
  preview: MetadataReviewPreview;
  applying: boolean;
  onApply: () => void;
}

const PreviewPanel: React.FC<PreviewPanelProps> = ({
  preview,
  applying,
  onApply,
}) => {
  const { t } = useTranslation("metadataReview");
  const metadata = preview.resolvedMetadata;
  const warningLabel = (warning: string): string => {
    if (warning === "notDownloaded") {
      return t("preview.warningCodes.notDownloaded");
    }
    if (warning === "unresolvedFiles") {
      return t("preview.warningCodes.unresolvedFiles");
    }
    if (warning === "collisionAdjusted") {
      return t("preview.collisionAdjusted");
    }
    return warning;
  };

  return (
    <section className="mt-6 border-t border-border pt-5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="font-serif text-base font-medium text-foreground">
            {t("preview.title")}
          </h3>
          <p className="mt-1 text-xs text-subtle">
            {t("preview.revisionAndExpiry", {
              revision: preview.baseRevision,
              expiresAt: new Date(preview.expiresAt).toLocaleTimeString(),
            })}
          </p>
        </div>
        {preview.canApply ? (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-success/10 px-2.5 py-1 text-xs font-medium text-success">
            <CheckCircle2 size={13} />
            {t("preview.ready")}
          </span>
        ) : (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-error/10 px-2.5 py-1 text-xs font-medium text-error">
            <AlertCircle size={13} />
            {t("preview.blocked")}
          </span>
        )}
      </div>

      <div className="mt-4 rounded-lg border border-border bg-canvas/50 p-4">
        <h4 className="text-xs font-medium uppercase tracking-wide text-subtle">
          {t("preview.resolvedMetadata")}
        </h4>
        <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div className="col-span-2">
            <dt className="text-xs text-subtle">{t("fields.name")}</dt>
            <dd className="mt-0.5 text-foreground">
              {metadata.name ?? t("values.notResolved")}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-subtle">{t("fields.tmdbId")}</dt>
            <dd className="mt-0.5 text-foreground">
              {metadata.tmdbId ?? t("values.unset")}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-subtle">{t("fields.groupName")}</dt>
            <dd className="mt-0.5 text-foreground">
              {metadata.groupName ?? t("values.unset")}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-subtle">{t("fields.season")}</dt>
            <dd className="mt-0.5 text-foreground">
              {metadata.season ?? t("values.unset")}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-subtle">{t("fields.episode")}</dt>
            <dd className="mt-0.5 text-foreground">
              {metadata.episode ?? t("values.unset")}
            </dd>
          </div>
        </dl>
      </div>

      {preview.warnings.length > 0 ? (
        <div className="mt-4 rounded-lg border border-warning/30 bg-warning/10 p-3 text-sm text-foreground">
          <div className="flex items-center gap-2 font-medium text-warning">
            <AlertTriangle size={15} />
            {t("preview.warnings")}
          </div>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted">
            {preview.warnings.map((warning, index) => (
              <li key={`${warning}-${index}`}>{warningLabel(warning)}</li>
            ))}
          </ul>
        </div>
      ) : null}

      <h4 className="mb-2 mt-5 text-sm font-medium text-foreground">
        {t("preview.pathChanges", { count: preview.pathChanges.length })}
      </h4>
      <PathDiff changes={preview.pathChanges} />

      <div className="mt-5 flex justify-end">
        <Button onClick={onApply} disabled={!preview.canApply || applying}>
          {applying ? <Spinner size={16} /> : <CheckCircle2 size={16} />}
          {applying ? t("editor.applying") : t("editor.apply")}
        </Button>
      </div>
    </section>
  );
};

export const MetadataReviewSheet: React.FC<MetadataReviewSheetProps> = ({
  item,
  onOpenChange,
  onApplied,
  restoreFocusRef,
}) => {
  const { t } = useTranslation("metadataReview");
  const [form, setForm] = React.useState<EditorFormValues>(emptyForm);
  const [fieldErrors, setFieldErrors] = React.useState<FieldErrors>({});
  const [preview, setPreview] = React.useState<MetadataReviewPreview | null>(
    null,
  );
  const [requestError, setRequestError] = React.useState<string | null>(null);
  const [previewing, setPreviewing] = React.useState(false);
  const [applying, setApplying] = React.useState(false);
  const formVersion = React.useRef(0);

  React.useEffect(() => {
    formVersion.current += 1;
    if (item) setForm(formFromItem(item));
    else setForm(emptyForm);
    setFieldErrors({});
    setPreview(null);
    setRequestError(null);
    setPreviewing(false);
    setApplying(false);
  }, [item?.id]);

  const updateField = React.useCallback((field: EditorField, value: string) => {
    formVersion.current += 1;
    setForm((current) => ({ ...current, [field]: value }));
    setFieldErrors((current) => ({ ...current, [field]: undefined }));
    setPreview(null);
    setRequestError(null);
  }, []);

  const validate = React.useCallback((): EditableMetadata | null => {
    const errors: FieldErrors = {};
    const tmdbId = form.tmdbId.trim();

    const parsedTmdbId = Number(tmdbId);
    if (
      !/^\d+$/.test(tmdbId) ||
      !Number.isSafeInteger(parsedTmdbId) ||
      parsedTmdbId <= 0 ||
      parsedTmdbId > 2_147_483_647
    ) {
      errors.tmdbId = t("editor.validation.tmdbId");
    }

    const parseIndex = (
      field: "season" | "episode",
      required: boolean,
    ): number | null => {
      const raw = form[field].trim();
      if (!raw) {
        if (required) errors[field] = t("editor.validation.seasonRequired");
        return null;
      }
      if (!/^\d+$/.test(raw) || !Number.isSafeInteger(Number(raw))) {
        errors[field] = t("editor.validation.nonNegativeInteger");
        return null;
      }
      return Number(raw);
    };

    const season = parseIndex("season", true);
    const episode = parseIndex("episode", false);
    const groupName = form.groupName.trim();
    if (groupName.length > 200) {
      errors.groupName = t("editor.validation.groupNameLength");
    }

    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) return null;

    return {
      tmdbId: tmdbId || null,
      season,
      episode,
      groupName: groupName || null,
    };
  }, [form, t]);

  const requestPreview = React.useCallback(async () => {
    if (!item) return;
    const metadata = validate();
    if (!metadata) return;

    const version = formVersion.current;
    setPreviewing(true);
    setRequestError(null);
    setPreview(null);
    try {
      const nextPreview = await previewMetadataReview(
        item.id,
        item.revision,
        metadata,
      );
      if (formVersion.current === version) setPreview(nextPreview);
    } catch (error) {
      if (formVersion.current === version) {
        setRequestError(t(apiErrorKey(error)));
      }
    } finally {
      setPreviewing(false);
    }
  }, [item, t, validate]);

  const applyPreview = React.useCallback(async () => {
    if (!item || !preview) return;
    setApplying(true);
    setRequestError(null);
    try {
      const result = await applyMetadataReview(item.id, preview.previewId);
      await onApplied(result);
    } catch (error) {
      setRequestError(t(apiErrorKey(error)));
    } finally {
      setApplying(false);
    }
  }, [item, onApplied, preview, t]);

  return (
    <Sheet open={item != null} onOpenChange={onOpenChange}>
      <SheetContent
        className="max-w-2xl"
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          restoreFocusRef.current?.focus();
        }}
      >
        <SheetHeader className="pr-14">
          <SheetTitle>{t("editor.title")}</SheetTitle>
          {item ? (
            <p
              className="mt-1 line-clamp-2 text-sm text-muted"
              title={item.title}
            >
              {item.title}
            </p>
          ) : null}
        </SheetHeader>

        <SheetBody>
          {item ? (
            <>
              <div className="rounded-lg border border-border-light bg-canvas/60 p-4">
                <div className="flex items-center gap-2 text-sm font-medium text-foreground">
                  <FileText size={15} className="text-muted" />
                  {t("editor.currentMetadata")}
                </div>
                <p className="mt-2 text-sm text-muted">
                  {item.metadata.name ?? t("values.notResolved")}
                  <span className="mx-2 text-border">/</span>
                  {t("editor.currentRevision", { revision: item.revision })}
                </p>
              </div>

              <div className="mt-5 space-y-4">
                <FormRow
                  label={t("fields.tmdbId")}
                  isInvalid={!!fieldErrors.tmdbId}
                  error={fieldErrors.tmdbId ? [fieldErrors.tmdbId] : undefined}
                >
                  <Input
                    value={form.tmdbId}
                    onChange={(event) =>
                      updateField("tmdbId", event.target.value)
                    }
                    inputMode="numeric"
                    placeholder={t("editor.placeholders.tmdbId")}
                    aria-label={t("fields.tmdbId")}
                    isInvalid={!!fieldErrors.tmdbId}
                  />
                </FormRow>

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <FormRow
                    label={t("fields.season")}
                    isInvalid={!!fieldErrors.season}
                    error={
                      fieldErrors.season ? [fieldErrors.season] : undefined
                    }
                  >
                    <Input
                      value={form.season}
                      onChange={(event) =>
                        updateField("season", event.target.value)
                      }
                      inputMode="numeric"
                      placeholder={t("editor.placeholders.season")}
                      aria-label={t("fields.season")}
                      isInvalid={!!fieldErrors.season}
                    />
                  </FormRow>
                  <FormRow
                    label={t("fields.episode")}
                    isInvalid={!!fieldErrors.episode}
                    error={
                      fieldErrors.episode ? [fieldErrors.episode] : undefined
                    }
                  >
                    <Input
                      value={form.episode}
                      onChange={(event) =>
                        updateField("episode", event.target.value)
                      }
                      inputMode="numeric"
                      placeholder={t("editor.placeholders.episode")}
                      aria-label={t("fields.episode")}
                      isInvalid={!!fieldErrors.episode}
                    />
                  </FormRow>
                </div>

                <FormRow
                  label={t("fields.groupName")}
                  isInvalid={!!fieldErrors.groupName}
                  error={
                    fieldErrors.groupName ? [fieldErrors.groupName] : undefined
                  }
                >
                  <Input
                    value={form.groupName}
                    onChange={(event) =>
                      updateField("groupName", event.target.value)
                    }
                    placeholder={t("editor.placeholders.groupName")}
                    aria-label={t("fields.groupName")}
                    isInvalid={!!fieldErrors.groupName}
                  />
                </FormRow>

                <p className="text-xs leading-body text-subtle">
                  {t("editor.unsetHint")}
                </p>
              </div>

              {requestError ? (
                <div
                  role="alert"
                  className="mt-4 flex items-start gap-2 rounded-lg border border-error/30 bg-error/10 p-3 text-sm text-error"
                >
                  <AlertCircle size={16} className="mt-0.5 shrink-0" />
                  <span>{requestError}</span>
                </div>
              ) : null}

              <span
                className="sr-only"
                role="status"
                aria-live="polite"
                aria-atomic="true"
              >
                {previewing
                  ? t("editor.previewing")
                  : preview
                    ? t("editor.previewReady")
                    : ""}
              </span>

              <div className="mt-5 flex justify-stretch sm:justify-end">
                <Button
                  variant="outline"
                  className="w-full sm:w-auto"
                  onClick={requestPreview}
                  disabled={previewing || applying}
                >
                  {previewing ? <Spinner size={16} /> : <FileText size={16} />}
                  {previewing
                    ? t("editor.previewing")
                    : preview
                      ? t("editor.refreshPreview")
                      : t("editor.preview")}
                </Button>
              </div>

              {preview ? (
                <PreviewPanel
                  preview={preview}
                  applying={applying}
                  onApply={applyPreview}
                />
              ) : null}
            </>
          ) : null}
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
};
