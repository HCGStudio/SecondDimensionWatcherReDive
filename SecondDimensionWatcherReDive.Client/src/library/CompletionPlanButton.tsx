import React from "react";
import { useTranslation } from "react-i18next";
import useSWR from "swr";

import { useAccess } from "../auth/hooks";
import fetcher from "../auth/httpClient";
import { Button } from "../components/ui/Button";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "../components/ui/Sheet";
import { Spinner } from "../components/ui/Spinner";

interface Candidate {
  releaseId: string;
  title: string;
  publishedAt: string;
  sizeBytes: number | null;
  score: number;
  reasons: string[];
  eligible: boolean;
  unavailableReason: string | null;
}
interface Episode {
  episode: number;
  state: string;
  airDate: string | null;
  selectedReleaseId: string | null;
  candidates: Candidate[];
  reason: string;
}
interface Plan {
  tmdbId: string;
  animationName: string;
  season: number;
  generatedAt: string;
  airDatesCheckedAt: string;
  airDatesSource: string;
  unidentifiedReleaseCount: number;
  episodes: Episode[];
}
interface Result {
  episode: number;
  releaseId: string;
  outcome: string;
  isSuccess: boolean;
}
const size = (bytes: number) => `${(bytes / 1024 ** 3).toFixed(2)} GiB`;

export const CompletionPlanButton: React.FC<{
  tmdbId: string;
  season: number;
}> = ({ tmdbId, season }) => {
  const { t } = useTranslation("library");
  const { canContentWrite } = useAccess();
  const [open, setOpen] = React.useState(false);
  const [selected, setSelected] = React.useState<Record<number, string>>({});
  const selectionPlan = React.useRef<string | null>(null);
  const [results, setResults] = React.useState<Result[]>([]);
  const [busy, setBusy] = React.useState(false);
  const [failure, setFailure] = React.useState(false);
  const { data, error, mutate, isLoading } = useSWR<Plan>(
    open
      ? `/api/library/completion?tmdbId=${encodeURIComponent(tmdbId)}&season=${season}`
      : null,
    fetcher,
  );
  React.useEffect(() => {
    if (!open) {
      selectionPlan.current = null;
      return;
    }
    if (!data || data.tmdbId !== tmdbId || data.season !== season) return;
    const planKey = `${tmdbId}:${season}`;
    const isNewPlan = selectionPlan.current !== planKey;
    selectionPlan.current = planKey;
    setSelected((old) =>
      Object.fromEntries(
        data.episodes.map((episode) => {
          const previous = old[episode.episode];
          const releaseId =
            !isNewPlan && previous !== undefined
              ? previous
              : (episode.selectedReleaseId ?? "");
          const selectable = ![
            "unaired",
            "downloaded",
            "downloading",
            "mapping_pending",
          ].includes(episode.state);
          return [
            episode.episode,
            selectable &&
            episode.candidates.some(
              (candidate) =>
                candidate.releaseId === releaseId && candidate.eligible,
            )
              ? releaseId
              : "",
          ];
        }),
      ),
    );
    if (isNewPlan) {
      setResults([]);
      setFailure(false);
    }
  }, [data, open, tmdbId, season]);
  const chosen =
    data?.episodes.flatMap((x) =>
      x.candidates.filter((c) => selected[x.episode] === c.releaseId),
    ) ?? [];
  const submit = async (onlyEpisode?: number) => {
    setBusy(true);
    setFailure(false);
    try {
      const response = await fetcher<Result[]>("/api/library/completion", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          tmdbId,
          season,
          selections: Object.entries(selected)
            .filter(
              ([episode, id]) =>
                id && (onlyEpisode == null || Number(episode) === onlyEpisode),
            )
            .map(([episode, releaseId]) => ({
              episode: Number(episode),
              releaseId,
            })),
        }),
      });
      setResults((old) => [
        ...old.filter((x) => !response.some((y) => y.episode === x.episode)),
        ...response,
      ]);
      await mutate();
    } catch {
      setFailure(true);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button size="sm" variant="outline">
          {t("completion.open")}
        </Button>
      </SheetTrigger>
      <SheetContent className="max-w-3xl" aria-describedby={undefined}>
        <SheetHeader>
          <SheetTitle>
            {t("completion.title")} · {data?.animationName ?? tmdbId}
          </SheetTitle>
        </SheetHeader>
        <SheetBody>
          <p className="mb-4 text-sm text-muted">
            {t("completion.description")}
          </p>
          {isLoading ? (
            <Spinner />
          ) : error ? (
            <p role="alert">{t("completion.error")}</p>
          ) : data ? (
            <>
              <p className="mb-3 text-xs text-subtle">
                {t("completion.calendar", {
                  source: data.airDatesSource,
                  date: new Date(data.airDatesCheckedAt).toLocaleString(),
                })}
              </p>
              {data.unidentifiedReleaseCount > 0 ? (
                <p className="mb-3 text-sm text-warning">
                  {t("completion.unidentified", {
                    count: data.unidentifiedReleaseCount,
                  })}
                </p>
              ) : null}
              {data.episodes.length === 0 ? (
                <p>{t("completion.empty")}</p>
              ) : null}
              <div className="space-y-3">
                {data.episodes.map((episode) => {
                  const candidate = episode.candidates.find(
                    (x) => x.releaseId === selected[episode.episode],
                  );
                  const result = results.find(
                    (x) => x.episode === episode.episode,
                  );
                  return (
                    <article
                      key={episode.episode}
                      className="rounded-lg border border-border p-4"
                    >
                      <div className="flex justify-between gap-2">
                        <h3 className="font-medium">
                          {t("completion.episode", {
                            episode: episode.episode,
                          })}
                        </h3>
                        <span className="text-sm text-brand">
                          {t(`completion.states.${episode.state}`)}
                        </span>
                      </div>
                      <p className="mt-1 text-xs text-muted">
                        {t("completion.airDate", {
                          date: episode.airDate ?? t("completion.unknown"),
                        })}
                      </p>
                      <p className="mt-1 text-xs text-muted">
                        {t(`completion.reasons.${episode.reason}`, {
                          defaultValue: episode.reason,
                        })}
                      </p>
                      {episode.candidates.length > 0 ? (
                        <label className="mt-3 block text-xs text-muted">
                          {t("completion.candidate")}
                          <select
                            className="mt-1 w-full rounded-md border border-border bg-surface p-2 text-foreground"
                            value={selected[episode.episode] ?? ""}
                            disabled={
                              busy ||
                              !canContentWrite ||
                              [
                                "unaired",
                                "downloaded",
                                "downloading",
                                "mapping_pending",
                              ].includes(episode.state)
                            }
                            onChange={(event) =>
                              setSelected((old) => ({
                                ...old,
                                [episode.episode]: event.target.value,
                              }))
                            }
                          >
                            <option value="">{t("completion.skip")}</option>
                            {episode.candidates.map((c) => (
                              <option
                                key={c.releaseId}
                                value={c.releaseId}
                                disabled={!c.eligible}
                              >
                                {c.title} · {c.score} ·{" "}
                                {c.sizeBytes == null
                                  ? t("completion.unknownSize")
                                  : size(c.sizeBytes)}
                                {c.unavailableReason
                                  ? ` · ${t(`completion.reasons.${c.unavailableReason}`)}`
                                  : ""}
                              </option>
                            ))}
                          </select>
                        </label>
                      ) : null}
                      {candidate ? (
                        <>
                          <p className="mt-2 text-xs text-muted">
                            {t("completion.published", {
                              date: new Date(
                                candidate.publishedAt,
                              ).toLocaleString(),
                            })}
                          </p>
                          <ul className="mt-1 text-xs text-subtle">
                            {candidate.reasons.map((reason, i) => (
                              <li key={i}>{reason}</li>
                            ))}
                          </ul>
                        </>
                      ) : null}
                      {result ? (
                        <div
                          className="mt-2 flex items-center gap-3 text-sm"
                          role="status"
                        >
                          <span
                            className={
                              result.isSuccess ? "text-success" : "text-error"
                            }
                          >
                            {t(`completion.outcomes.${result.outcome}`)}
                          </span>
                          {!result.isSuccess &&
                          canContentWrite &&
                          selected[episode.episode] ? (
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={busy}
                              onClick={() => void submit(episode.episode)}
                            >
                              {t("completion.retry")}
                            </Button>
                          ) : null}
                        </div>
                      ) : null}
                    </article>
                  );
                })}
              </div>
            </>
          ) : null}
          {failure ? (
            <p role="alert" className="mt-3 text-error">
              {t("completion.error")}
            </p>
          ) : null}
        </SheetBody>
        {canContentWrite ? (
          <div className="border-t border-border p-4">
            <p className="mb-3 text-xs text-muted">
              {t("completion.total", {
                count: chosen.length,
                size: size(
                  chosen.reduce((sum, c) => sum + (c.sizeBytes ?? 0), 0),
                ),
                unknown: chosen.filter((c) => c.sizeBytes == null).length,
              })}
            </p>
            <Button
              disabled={busy || chosen.length === 0}
              onClick={() => void submit()}
            >
              {busy ? <Spinner size={16} /> : null}
              {t("completion.submit")}
            </Button>
          </div>
        ) : null}
      </SheetContent>
    </Sheet>
  );
};
