import React from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { PreviewPanel } from "../components/MetadataReviewSheet";
import { Button } from "../components/ui/Button";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "../components/ui/Sheet";
import { Spinner } from "../components/ui/Spinner";
import { ApiError } from "../errors/apiError";
import { useFeeds } from "../feed/hooks";
import { applyMetadataReview } from "./api";
import {
  RecognitionHit,
  RecognitionPreview,
  RecognitionRule,
  RecognitionRuleDraft,
  RecognitionSample,
  RecognitionSeed,
  ruleRequest,
} from "./rules";
import { MetadataReviewPreview } from "./types";

const emptyDraft: RecognitionRuleDraft = {
  name: "",
  enabled: true,
  sourceFeedId: null,
  titlePattern: null,
  subtitleGroup: null,
  tmdbId: "",
  fixedSeason: null,
  episodeOffset: 0,
  canonicalGroupName: null,
  createdFromItemId: null,
  expectedRevision: null,
};

export const MetadataRulesPanel: React.FC<{
  sourceItemId: string | null;
  onSourceConsumed: () => void;
  onHistoryApplied: () => Promise<unknown>;
}> = ({ sourceItemId, onSourceConsumed, onHistoryApplied }) => {
  const { t } = useTranslation("metadataReview");
  const {
    data: rules,
    error: rulesError,
    mutate,
  } = useSWR<RecognitionRule[]>("/api/metadata-rules", fetcher);
  const { data: hits, error: hitsError } = useSWR<RecognitionHit[]>(
    "/api/metadata-rules/hits",
    fetcher,
    { refreshInterval: 30000 },
  );
  const { data: feeds } = useFeeds();
  const [draft, setDraft] = React.useState<RecognitionRuleDraft | null>(null);
  const [editingId, setEditingId] = React.useState<string | null>(null);
  const [preview, setPreview] = React.useState<RecognitionPreview | null>(null);
  const [savedNotice, setSavedNotice] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [history, setHistory] = React.useState<{
    itemId: string;
    preview: MetadataReviewPreview;
  } | null>(null);
  const version = React.useRef(0);
  const editorRef = React.useRef<HTMLDivElement>(null);

  const showError = React.useCallback(
    (cause: unknown) => {
      const code = cause instanceof ApiError ? cause.code : "requestFailed";
      setError(
        t(`rules.errors.${code}`, {
          defaultValue: t("rules.errors.requestFailed"),
        }),
      );
    },
    [t],
  );

  React.useEffect(() => {
    if (!sourceItemId) return;
    let active = true;
    setBusy(true);
    setError(null);
    fetcher<RecognitionSeed>(
      `/api/metadata-rules/from-correction/${sourceItemId}`,
    )
      .then((seed) => {
        if (!active) return;
        version.current++;
        setEditingId(null);
        setPreview(null);
        setSavedNotice(false);
        // The scope remains an explicit user decision; never derive a regex from a correction.
        setDraft({
          ...emptyDraft,
          name: seed.title.slice(0, 200),
          sourceFeedId: seed.sourceFeedId,
          tmdbId: seed.tmdbId ?? "",
          fixedSeason: seed.season,
          canonicalGroupName: seed.groupName,
          createdFromItemId: seed.itemId,
        });
        requestAnimationFrame(() =>
          editorRef.current?.scrollIntoView({
            behavior: "smooth",
            block: "start",
          }),
        );
      })
      .catch((cause) => {
        if (active) showError(cause);
      })
      .finally(() => {
        if (active) {
          setBusy(false);
          onSourceConsumed();
        }
      });
    return () => {
      active = false;
    };
  }, [sourceItemId, onSourceConsumed, showError]);

  function edit(rule?: RecognitionRule) {
    version.current++;
    setEditingId(rule?.id ?? null);
    setDraft(
      rule ? { ...rule, expectedRevision: rule.revision } : { ...emptyDraft },
    );
    setPreview(null);
    setError(null);
    setSavedNotice(false);
  }

  function update<K extends keyof RecognitionRuleDraft>(
    key: K,
    value: RecognitionRuleDraft[K],
  ) {
    version.current++;
    setDraft((current) => (current ? { ...current, [key]: value } : current));
    setPreview(null);
    setSavedNotice(false);
    setError(null);
  }

  async function previewRule() {
    if (!draft) return;
    const requestedVersion = version.current;
    setBusy(true);
    setError(null);
    try {
      const next = await ruleRequest<RecognitionPreview>(
        `/api/metadata-rules/preview${editingId ? `?id=${editingId}` : ""}`,
        draft,
      );
      if (version.current === requestedVersion) setPreview(next);
    } catch (cause) {
      showError(cause);
    } finally {
      setBusy(false);
    }
  }

  async function saveRule() {
    if (!draft) return;
    setBusy(true);
    setError(null);
    try {
      const saved = await ruleRequest<RecognitionRule>(
        `/api/metadata-rules${editingId ? `/${editingId}` : ""}`,
        draft,
        editingId ? "PUT" : "POST",
      );
      setEditingId(saved.id);
      setDraft({ ...saved, expectedRevision: saved.revision });
      setSavedNotice(true);
      await mutate();
    } catch (cause) {
      showError(cause);
    } finally {
      setBusy(false);
    }
  }

  async function toggleRule(rule: RecognitionRule) {
    setBusy(true);
    setError(null);
    try {
      await ruleRequest(
        `/api/metadata-rules/${rule.id}`,
        { ...rule, enabled: !rule.enabled, expectedRevision: rule.revision },
        "PUT",
      );
      version.current++;
      setPreview(null);
      setHistory(null);
      if (editingId === rule.id) {
        setDraft(null);
      }
      await mutate();
    } catch (cause) {
      showError(cause);
    } finally {
      setBusy(false);
    }
  }

  async function previewHistory(sample: RecognitionSample) {
    if (!editingId || !draft?.expectedRevision || !selectedSavedRule?.enabled)
      return;
    const requestedVersion = version.current;
    setBusy(true);
    setError(null);
    try {
      const result = await ruleRequest<MetadataReviewPreview>(
        `/api/metadata-rules/${editingId}/history/${sample.itemId}/preview`,
        {
          ruleRevision: draft.expectedRevision,
          itemRevision: sample.revision,
        },
      );
      if (version.current === requestedVersion)
        setHistory({ itemId: sample.itemId, preview: result });
    } catch (cause) {
      showError(cause);
    } finally {
      setBusy(false);
    }
  }

  async function applyHistory() {
    if (!history) return;
    setBusy(true);
    setError(null);
    try {
      await applyMetadataReview(history.itemId, history.preview.previewId);
      setHistory(null);
      setPreview(null);
      await onHistoryApplied();
    } catch (cause) {
      showError(cause);
    } finally {
      setBusy(false);
    }
  }

  const selectedSavedRule = rules?.find((rule) => rule.id === editingId);
  const draftMatchesSaved =
    !!draft &&
    !!selectedSavedRule &&
    Object.keys(emptyDraft)
      .filter((key) => key !== "expectedRevision")
      .every(
        (key) =>
          draft[key as keyof RecognitionRuleDraft] ===
          selectedSavedRule[key as keyof RecognitionRuleDraft],
      );
  const numberInput = (key: "fixedSeason" | "episodeOffset") => (
    <Input
      type="number"
      min={key === "fixedSeason" ? 0 : -10000}
      max={key === "episodeOffset" ? 10000 : 2147483647}
      step={1}
      value={draft?.[key] ?? ""}
      disabled={busy}
      aria-label={t(`rules.${key}`)}
      onChange={(event) =>
        update(
          key,
          event.target.value === ""
            ? key === "fixedSeason"
              ? null
              : 0
            : Number(event.target.value),
        )
      }
    />
  );

  return (
    <section className="mt-6 rounded-xl border border-border-light bg-surface p-4 shadow-whisper sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-serif text-lg text-foreground">
            {t("rules.title")}
          </h2>
          <p className="mt-1 max-w-3xl text-sm text-muted">
            {t("rules.subtitle")}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          onClick={() => edit()}
          disabled={busy}
        >
          {t("rules.create")}
        </Button>
      </div>
      <p className="mt-3 text-xs text-subtle">{t("rules.undoHint")}</p>
      {(error || rulesError || hitsError) && (
        <p role="alert" className="mt-3 text-sm text-error">
          {error ?? t("rules.errors.requestFailed")}
        </p>
      )}
      {!rules && !rulesError && <Spinner />}
      {rules?.length === 0 && (
        <p className="mt-4 text-sm text-subtle">{t("rules.empty")}</p>
      )}
      <ul className="mt-4 divide-y divide-border-light">
        {rules?.map((rule) => (
          <li
            key={rule.id}
            className="flex flex-wrap items-center justify-between gap-3 py-3"
          >
            <div className="min-w-0">
              <p className="break-words text-sm font-medium">
                {rule.name}{" "}
                <span className="ml-2 text-xs text-muted">
                  {t(rule.enabled ? "rules.enabled" : "rules.disabled")}
                </span>
              </p>
              <p className="mt-1 text-xs text-muted">
                TMDB {rule.tmdbId} ·{" "}
                {t("rules.revision", { revision: rule.revision })} ·{" "}
                {t("rules.effectiveFrom", {
                  time: new Date(rule.effectiveFrom).toLocaleString(),
                })}
              </p>
            </div>
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() => edit(rule)}
              >
                {t("rules.edit")}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() => void toggleRule(rule)}
              >
                {t(rule.enabled ? "rules.disable" : "rules.enable")}
              </Button>
            </div>
          </li>
        ))}
      </ul>
      {draft && (
        <div ref={editorRef} className="mt-4 border-t border-border pt-5">
          <h3 className="font-serif text-base">
            {t(editingId ? "rules.edit" : "rules.create")}
          </h3>
          <fieldset disabled={busy} className="mt-4 grid gap-4 sm:grid-cols-2">
            <FormRow label={t("rules.name")}>
              <Input
                maxLength={200}
                value={draft.name}
                aria-label={t("rules.name")}
                onChange={(event) => update("name", event.target.value)}
              />
            </FormRow>
            <FormRow label={t("rules.source")}>
              <select
                className="w-full rounded-lg border border-border bg-canvas px-3 py-2 text-sm"
                aria-label={t("rules.source")}
                value={draft.sourceFeedId ?? ""}
                onChange={(event) =>
                  update("sourceFeedId", event.target.value || null)
                }
              >
                <option value="">{t("rules.anySource")}</option>
                {feeds?.map((feed) => (
                  <option key={feed.id} value={feed.id}>
                    {feed.name || feed.url}
                  </option>
                ))}
                {draft.sourceFeedId &&
                  !feeds?.some((feed) => feed.id === draft.sourceFeedId) && (
                    <option value={draft.sourceFeedId}>
                      {draft.sourceFeedId}
                    </option>
                  )}
              </select>
            </FormRow>
            <FormRow label={t("rules.pattern")} className="sm:col-span-2">
              <Input
                value={draft.titlePattern ?? ""}
                maxLength={1000}
                aria-label={t("rules.pattern")}
                onChange={(event) =>
                  update("titlePattern", event.target.value || null)
                }
              />
              <p className="mt-1 text-xs text-subtle">
                {t("rules.patternHint")}
              </p>
            </FormRow>
            <FormRow label={t("rules.subtitleGroup")}>
              <Input
                value={draft.subtitleGroup ?? ""}
                maxLength={200}
                aria-label={t("rules.subtitleGroup")}
                onChange={(event) =>
                  update("subtitleGroup", event.target.value || null)
                }
              />
            </FormRow>
            <FormRow label={t("fields.tmdbId")}>
              <Input
                value={draft.tmdbId}
                inputMode="numeric"
                aria-label={t("fields.tmdbId")}
                onChange={(event) => update("tmdbId", event.target.value)}
              />
            </FormRow>
            <FormRow label={t("rules.fixedSeason")}>
              {numberInput("fixedSeason")}
            </FormRow>
            <FormRow label={t("rules.episodeOffset")}>
              {numberInput("episodeOffset")}
            </FormRow>
            <FormRow label={t("rules.canonicalGroupName")}>
              <Input
                value={draft.canonicalGroupName ?? ""}
                maxLength={200}
                aria-label={t("rules.canonicalGroupName")}
                onChange={(event) =>
                  update("canonicalGroupName", event.target.value || null)
                }
              />
            </FormRow>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={draft.enabled}
                onChange={(event) => update("enabled", event.target.checked)}
              />
              {t("rules.enabled")}
            </label>
          </fieldset>
          <p className="mt-4 text-xs text-subtle">{t("rules.scopeHint")}</p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Button
              variant="outline"
              onClick={() => void previewRule()}
              disabled={busy}
            >
              {t("rules.preview")}
            </Button>
            <Button onClick={() => void saveRule()} disabled={busy || !preview}>
              {t("rules.save")}
            </Button>
            <Button
              variant="ghost"
              disabled={busy}
              onClick={() => {
                setDraft(null);
                setPreview(null);
              }}
            >
              {t("rules.close")}
            </Button>
          </div>
          {savedNotice && (
            <p role="status" className="mt-3 text-sm text-success">
              {t("rules.saved")}
            </p>
          )}
          {preview && (
            <div className="mt-5">
              <h4 className="font-medium">
                {t("rules.previewCounts", {
                  matches: preview.matchCount,
                  scanned: preview.scannedCount,
                })}
              </h4>
              <p className="mt-1 text-xs text-muted">
                {t("rules.previewHint")}
                {preview.truncated ? ` ${t("rules.truncated")}` : ""}
              </p>
              <ul className="mt-3 divide-y divide-border-light">
                {preview.samples.map((sample) => (
                  <li key={sample.itemId} className="py-3">
                    <p className="text-sm font-medium">{sample.title}</p>
                    <p className="mt-1 break-words text-xs text-muted">
                      TMDB {sample.currentTmdbId ?? "—"} / S
                      {sample.currentSeason ?? "—"}E
                      {sample.currentEpisode ?? "—"} /{" "}
                      {sample.currentGroupName ?? "—"} → TMDB {sample.tmdbId} /
                      S{sample.season ?? "—"}E{sample.episode ?? "—"} /{" "}
                      {sample.groupName ?? "—"}
                    </p>
                    {sample.warning && (
                      <p className="mt-1 text-xs text-warning">
                        {t(`rules.warnings.${sample.warning}`)}
                      </p>
                    )}
                    {draftMatchesSaved && selectedSavedRule?.enabled && (
                      <Button
                        className="mt-2"
                        size="sm"
                        variant="outline"
                        disabled={busy || !!sample.warning}
                        onClick={() => void previewHistory(sample)}
                      >
                        {t("rules.historyPreview")}
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
      <details className="mt-5 border-t border-border-light pt-4">
        <summary className="cursor-pointer text-sm font-medium">
          {t("rules.hits")}
        </summary>
        <p className="mt-2 text-xs text-muted">{t("rules.hitsHint")}</p>
        {hits?.length === 0 && (
          <p className="mt-3 text-sm text-subtle">{t("rules.noHits")}</p>
        )}
        <ul className="mt-3 divide-y divide-border-light">
          {hits?.map((hit) => (
            <li key={hit.id} className="py-2 text-sm">
              <p>{hit.title}</p>
              <p className="mt-1 text-xs text-muted">
                {hit.ruleName} ·{" "}
                {t("rules.hitRevision", {
                  rule: hit.ruleRevision,
                  item: hit.itemRevision,
                })}{" "}
                · {new Date(hit.appliedAt).toLocaleString()}
              </p>
              <Link
                className="text-xs text-brand"
                to={`/metadata-review?focus=${hit.animationInfoId}`}
              >
                {t("rules.inspectHit")}
              </Link>
            </li>
          ))}
        </ul>
      </details>
      <Sheet
        open={!!history}
        onOpenChange={(open) => {
          if (!open && !busy) setHistory(null);
        }}
      >
        <SheetContent className="max-w-2xl">
          <SheetHeader>
            <SheetTitle>{t("rules.historyPreview")}</SheetTitle>
          </SheetHeader>
          <SheetBody>
            {error && (
              <p role="alert" className="text-sm text-error">
                {error}
              </p>
            )}
            {history && (
              <PreviewPanel
                preview={history.preview}
                applying={busy}
                onApply={() => void applyHistory()}
              />
            )}
          </SheetBody>
        </SheetContent>
      </Sheet>
    </section>
  );
};
