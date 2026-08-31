import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import {
  AlertTriangle,
  CheckCircle2,
  ChevronRight,
  Download,
  FileQuestion,
  RefreshCw,
  Search,
} from "lucide-react";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Input } from "../components/ui/Input";
import { Spinner } from "../components/ui/Spinner";
import { cn } from "../lib/cn";
import {
  executeReleaseUpgrade,
  useLibraryIntegrity,
  useLibrarySearch,
} from "../library/api";
import { ReleaseUpgradeCandidate } from "../library/types";
import { PageTemplate } from "./PageTemplate";

const FILTER_KEYS = [
  "season",
  "episode",
  "subtitleGroup",
  "resolution",
  "codec",
  "language",
  "downloadState",
  "watchState",
  "path",
  "source",
  "sort",
] as const;

const selectClass =
  "w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-foreground focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus";

export const SearchPage: React.FC = () => {
  const { t } = useTranslation("library");
  const { addToast } = useToast();
  const [params, setParams] = useSearchParams();
  const [query, setQuery] = React.useState(params.get("q") ?? "");
  const [runningUpgrade, setRunningUpgrade] = React.useState<string | null>(
    null,
  );
  const { data, error, isLoading } = useLibrarySearch(params);
  const { data: integrity, mutate: refreshIntegrity } = useLibraryIntegrity();

  React.useEffect(() => setQuery(params.get("q") ?? ""), [params]);

  const setFilter = React.useCallback(
    (key: string, value: string) => {
      const next = new URLSearchParams(params);
      if (value) next.set(key, value);
      else next.delete(key);
      if (key !== "cursor") next.delete("cursor");
      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const submitQuery = (event: React.FormEvent) => {
    event.preventDefault();
    setFilter("q", query.trim());
  };

  const clearFilters = () => {
    setQuery("");
    setParams(new URLSearchParams(), { replace: true });
  };

  const runUpgrade = async (
    candidate: ReleaseUpgradeCandidate,
    dryRun: boolean,
  ) => {
    if (!dryRun && !window.confirm(t("upgrade.confirm"))) return;
    setRunningUpgrade(candidate.candidateReleaseId);
    try {
      const result = await executeReleaseUpgrade(candidate, dryRun);
      addToast({
        title: dryRun
          ? result.requiresDownload
            ? t("upgrade.previewNeedsDownload")
            : t("upgrade.previewReady")
          : t(`upgrade.outcomes.${result.outcome}`, {
              defaultValue: result.outcome,
            }),
        color: result.isSuccess ? "success" : "danger",
      });
      if (!dryRun) await refreshIntegrity();
    } catch {
      addToast({ title: t("upgrade.failed"), color: "danger" });
    } finally {
      setRunningUpgrade(null);
    }
  };

  const activeFilterCount = FILTER_KEYS.filter((key) => params.has(key)).length;

  return (
    <PageTemplate>
      <header className="mb-7 max-w-3xl">
        <p className="font-mono text-xs uppercase tracking-[0.18em] text-brand">
          {t("eyebrow")}
        </p>
        <h1 className="mt-2 font-serif text-3xl font-medium text-foreground">
          {t("title")}
        </h1>
        <p className="mt-2 text-sm leading-body text-muted">
          {t("description")}
        </p>
      </header>

      <form onSubmit={submitQuery} className="flex gap-2">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-2.5 text-subtle" size={18} />
          <Input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            className="pl-10"
            placeholder={t("queryPlaceholder")}
            aria-label={t("queryLabel")}
          />
        </div>
        <Button type="submit">{t("search")}</Button>
      </form>

      <section className="mt-4 rounded-xl border border-border-light bg-surface p-4 shadow-whisper">
        <div className="mb-3 flex items-center justify-between gap-3">
          <h2 className="text-sm font-medium text-foreground">
            {t("filters.title", { count: activeFilterCount })}
          </h2>
          <button
            type="button"
            onClick={clearFilters}
            className="text-xs text-brand hover:underline"
          >
            {t("filters.clear")}
          </button>
        </div>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <FilterInput
            label={t("filters.season")}
            name="season"
            params={params}
            onChange={setFilter}
            type="number"
          />
          <FilterInput
            label={t("filters.episode")}
            name="episode"
            params={params}
            onChange={setFilter}
            type="number"
          />
          <FilterInput
            label={t("filters.subtitleGroup")}
            name="subtitleGroup"
            params={params}
            onChange={setFilter}
          />
          <FilterInput
            label={t("filters.path")}
            name="path"
            params={params}
            onChange={setFilter}
          />
          <FilterInput
            label={t("filters.resolution")}
            name="resolution"
            params={params}
            onChange={setFilter}
          />
          <FilterInput
            label={t("filters.codec")}
            name="codec"
            params={params}
            onChange={setFilter}
          />
          <FilterInput
            label={t("filters.language")}
            name="language"
            params={params}
            onChange={setFilter}
          />
          <FilterSelect
            label={t("filters.downloadState")}
            name="downloadState"
            params={params}
            onChange={setFilter}
            options={["Any", "NotDownloaded", "Downloading", "Downloaded"]}
            t={t}
          />
          <FilterSelect
            label={t("filters.watchState")}
            name="watchState"
            params={params}
            onChange={setFilter}
            options={["Any", "Unwatched", "InProgress", "Watched"]}
            t={t}
          />
          <FilterSelect
            label={t("filters.source")}
            name="source"
            params={params}
            onChange={setFilter}
            options={["Any", "Torrent", "MediaLibraryImport"]}
            t={t}
          />
          <FilterSelect
            label={t("filters.sort")}
            name="sort"
            params={params}
            onChange={setFilter}
            options={[
              "PublishedDescending",
              "TitleAscending",
              "EpisodeAscending",
              "ScoreDescending",
            ]}
            t={t}
          />
        </div>
      </section>

      <section className="mt-8">
        <div className="mb-3 flex items-end justify-between gap-3">
          <h2 className="font-serif text-xl font-medium text-foreground">
            {t("results.title")}
          </h2>
          {data ? (
            <span className="text-xs text-subtle">
              {t("results.pageCount", { count: data.items.length })}
            </span>
          ) : null}
        </div>
        {isLoading ? (
          <div className="flex justify-center py-16">
            <Spinner />
          </div>
        ) : error ? (
          <EmptyPrompt icon={<AlertTriangle />} title={t("results.error")} />
        ) : !data?.items.length ? (
          <EmptyPrompt icon={<FileQuestion />} title={t("results.empty")} />
        ) : (
          <div className="divide-y divide-border-light overflow-hidden rounded-xl border border-border bg-surface shadow-whisper">
            {data.items.map((item) => (
              <article key={item.animationInfoId} className="p-4 sm:p-5">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2 text-xs text-subtle">
                      <span className="rounded-full bg-brand/10 px-2 py-0.5 text-brand">
                        {item.season != null && item.episode != null
                          ? `S${String(item.season).padStart(2, "0")}E${String(item.episode).padStart(2, "0")}`
                          : t("results.unidentified")}
                      </span>
                      <span>
                        {item.isMediaLibraryImport
                          ? t("values.MediaLibraryImport")
                          : t("values.Torrent")}
                      </span>
                      <span>
                        {new Date(item.publishedAt).toLocaleDateString()}
                      </span>
                    </div>
                    <h3 className="mt-2 font-serif text-lg font-medium text-foreground">
                      {item.animationName ?? item.title}
                    </h3>
                    {item.animationOriginalName ? (
                      <p className="text-sm text-muted">
                        {item.animationOriginalName}
                      </p>
                    ) : null}
                    <div className="mt-2 flex flex-wrap gap-1.5 text-xs text-muted">
                      {[
                        item.subtitleGroup,
                        item.resolution,
                        item.codec,
                        ...item.languages,
                      ]
                        .filter(Boolean)
                        .map((value) => (
                          <span
                            key={value}
                            className="rounded bg-canvas px-2 py-1"
                          >
                            {value}
                          </span>
                        ))}
                    </div>
                    {item.virtualPaths.map((path) => (
                      <p
                        key={path}
                        className="mt-2 break-all font-mono text-xs text-subtle"
                      >
                        {path}
                      </p>
                    ))}
                    {item.virtualPathCount > item.virtualPaths.length ? (
                      <p className="mt-2 text-xs text-subtle">
                        {t("results.morePaths", {
                          count:
                            item.virtualPathCount - item.virtualPaths.length,
                        })}
                      </p>
                    ) : null}
                  </div>
                  <div className="shrink-0 text-left sm:text-right">
                    <p className="font-mono text-lg font-medium text-foreground">
                      {item.releaseScore}
                    </p>
                    <p className="text-xs text-subtle">{t("results.score")}</p>
                    <p
                      className={cn(
                        "mt-2 text-xs",
                        item.isDownloadFinished ? "text-success" : "text-muted",
                      )}
                    >
                      {item.isDownloadFinished
                        ? t("values.Downloaded")
                        : item.isDownloadTracked
                          ? t("values.Downloading")
                          : t("values.NotDownloaded")}
                    </p>
                  </div>
                </div>
                {item.scoreReasons.length ? (
                  <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-subtle">
                    {item.scoreReasons.map((reason) => (
                      <li key={reason}>+ {reason}</li>
                    ))}
                  </ul>
                ) : null}
              </article>
            ))}
          </div>
        )}
        {data?.nextCursor ? (
          <div className="mt-4 flex justify-end">
            <Button
              variant="outline"
              onClick={() => setFilter("cursor", data.nextCursor!)}
            >
              {t("results.next")} <ChevronRight size={16} />
            </Button>
          </div>
        ) : null}
      </section>

      <section className="mt-12">
        <div className="mb-4 flex items-end justify-between gap-3">
          <div>
            <h2 className="font-serif text-xl font-medium text-foreground">
              {t("integrity.title")}
            </h2>
            <p className="mt-1 text-sm text-muted">
              {t("integrity.description")}
            </p>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => void refreshIntegrity()}
          >
            <RefreshCw size={15} /> {t("integrity.refresh")}
          </Button>
        </div>
        <div className="grid gap-4 lg:grid-cols-2">
          {integrity
            ?.filter(
              (item) =>
                item.missingEpisodes.length ||
                item.duplicateEpisodes.length ||
                item.unidentifiedReleaseCount ||
                item.upgradeCandidates.length,
            )
            .map((item) => (
              <article
                key={`${item.tmdbId}-${item.season}`}
                className="rounded-xl border border-border bg-surface p-5 shadow-whisper"
              >
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="font-serif text-lg font-medium text-foreground">
                      {item.animationName}
                    </h3>
                    <p className="text-xs text-subtle">
                      {t("integrity.season", {
                        season: item.season,
                        count: item.expectedEpisodeCount ?? "?",
                      })}
                    </p>
                  </div>
                  {item.missingEpisodes.length === 0 &&
                  item.duplicateEpisodes.length === 0 &&
                  item.unidentifiedReleaseCount === 0 ? (
                    <CheckCircle2 className="text-success" size={20} />
                  ) : (
                    <AlertTriangle className="text-warning" size={20} />
                  )}
                </div>
                <dl className="mt-4 grid grid-cols-3 gap-3 text-center">
                  <Metric
                    label={t("integrity.missing")}
                    value={item.missingEpisodes.join(", ") || "—"}
                  />
                  <Metric
                    label={t("integrity.duplicates")}
                    value={
                      item.duplicateEpisodes
                        .map((entry) => entry.episode)
                        .join(", ") || "—"
                    }
                  />
                  <Metric
                    label={t("integrity.unidentified")}
                    value={String(item.unidentifiedReleaseCount)}
                  />
                </dl>
                {item.upgradeCandidates.map((candidate) => (
                  <div
                    key={candidate.candidateReleaseId}
                    className="mt-4 rounded-lg border border-border-light bg-canvas/50 p-3"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div>
                        <p className="text-sm font-medium text-foreground">
                          {t("upgrade.episode", { episode: candidate.episode })}
                        </p>
                        <p className="text-xs text-muted">
                          {candidate.currentScore} → {candidate.candidateScore}{" "}
                          (+{candidate.candidateScore - candidate.currentScore})
                        </p>
                      </div>
                      <div className="flex gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={
                            runningUpgrade === candidate.candidateReleaseId
                          }
                          onClick={() => void runUpgrade(candidate, true)}
                        >
                          {t("upgrade.preview")}
                        </Button>
                        <Button
                          size="sm"
                          disabled={
                            runningUpgrade === candidate.candidateReleaseId
                          }
                          onClick={() => void runUpgrade(candidate, false)}
                        >
                          {runningUpgrade === candidate.candidateReleaseId ? (
                            <Spinner className="h-4 w-4" />
                          ) : (
                            <Download size={14} />
                          )}
                          {t("upgrade.apply")}
                        </Button>
                      </div>
                    </div>
                    <ul className="mt-2 text-xs text-subtle">
                      {candidate.scoreReasons.map((reason) => (
                        <li key={reason}>+ {reason}</li>
                      ))}
                    </ul>
                  </div>
                ))}
              </article>
            ))}
        </div>
      </section>
    </PageTemplate>
  );
};

const FilterInput: React.FC<{
  label: string;
  name: string;
  params: URLSearchParams;
  onChange: (name: string, value: string) => void;
  type?: string;
}> = ({ label, name, params, onChange, type = "text" }) => (
  <label className="text-xs text-muted">
    {label}
    <Input
      key={`${name}-${params.get(name) ?? ""}`}
      type={type}
      min={type === "number" ? 0 : undefined}
      defaultValue={params.get(name) ?? ""}
      className="mt-1"
      onBlur={(event) => onChange(name, event.target.value.trim())}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          onChange(name, event.currentTarget.value.trim());
        }
      }}
    />
  </label>
);

const FilterSelect: React.FC<{
  label: string;
  name: string;
  params: URLSearchParams;
  onChange: (name: string, value: string) => void;
  options: string[];
  t: (key: string) => string;
}> = ({ label, name, params, onChange, options, t }) => (
  <label className="text-xs text-muted">
    {label}
    <select
      className={`${selectClass} mt-1`}
      value={params.get(name) ?? options[0]}
      onChange={(event) =>
        onChange(
          name,
          event.target.value === options[0] ? "" : event.target.value,
        )
      }
    >
      {options.map((option) => (
        <option key={option} value={option}>
          {t(`values.${option}`)}
        </option>
      ))}
    </select>
  </label>
);

const Metric: React.FC<{ label: string; value: string }> = ({
  label,
  value,
}) => (
  <div className="rounded-lg bg-canvas px-2 py-3">
    <dt className="text-xs text-subtle">{label}</dt>
    <dd className="mt-1 break-words font-mono text-sm text-foreground">
      {value}
    </dd>
  </div>
);
