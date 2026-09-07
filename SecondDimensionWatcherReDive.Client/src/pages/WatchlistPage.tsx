import React from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";

import { useAccess } from "../auth/hooks";
import fetcher from "../auth/httpClient";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { Spinner } from "../components/ui/Spinner";
import {
  WatchlistItem,
  currentWeek,
  refreshWatchlist,
  saveWatchlist,
  useWatchlist,
  watchlistStatuses,
} from "../watchlist/hooks";
import { PageTemplate } from "./PageTemplate";

export const WatchlistPage: React.FC = () => {
  const { t, i18n } = useTranslation("watchlist");
  const { canPlaybackWrite } = useAccess();
  const [week, setWeek] = React.useState(currentWeek);
  const [filter, setFilter] = React.useState("all");
  const { data, error, isLoading } = useWatchlist(week.toISOString());
  const { addToast } = useToast();
  const [busy, setBusy] = React.useState<string | null>(null);
  const [linking, setLinking] = React.useState<string | null>(null);
  const [tmdb, setTmdb] = React.useState("");
  const [mikan, setMikan] = React.useState("");
  const act = async (id: string, callback: () => Promise<unknown>) => {
    setBusy(id);
    try {
      await callback();
    } catch {
      addToast({ title: t("failed"), color: "danger" });
    } finally {
      setBusy(null);
    }
  };
  const dateLabel = (date: Date) =>
    date.toLocaleDateString(i18n.language, { month: "short", day: "numeric" });
  const dayLabel = (day: number) =>
    new Date(2024, 0, 7 + day).toLocaleDateString(i18n.language, {
      weekday: "long",
    });
  const moveWeek = (amount: number) =>
    setWeek((value) => {
      const next = new Date(value);
      next.setDate(next.getDate() + amount * 7);
      return next;
    });
  const visible =
    data?.filter((item) => filter === "all" || item.status === filter) ?? [];
  const calendar = visible.filter(
    (item) => item.status === "watching" || item.status === "planned",
  );
  const renderItem = (item: WatchlistItem) => (
    <article
      key={item.id}
      className="rounded-xl border border-border bg-surface p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        {item.tmdbId ? (
          <Link
            className="font-serif text-lg text-brand"
            to={`/anime/${item.tmdbId}`}
          >
            {item.title}
          </Link>
        ) : (
          <h2 className="font-serif text-lg">{item.title}</h2>
        )}
        <div className="flex flex-wrap gap-2">
          <select
            aria-label={t("status")}
            className="rounded border border-border bg-canvas p-2 text-sm"
            value={item.status}
            disabled={!canPlaybackWrite || busy === item.id}
            onChange={(e) =>
              void act(item.id, () =>
                saveWatchlist({ ...item, status: e.target.value }),
              )
            }
          >
            {watchlistStatuses.map((status) => (
              <option key={status} value={status}>
                {t(`statuses.${status}`)}
              </option>
            ))}
          </select>
          {canPlaybackWrite && (
            <>
              <Button
                variant="outline"
                size="sm"
                disabled={busy === item.id}
                onClick={() => {
                  setLinking(linking === item.id ? null : item.id);
                  setTmdb(item.tmdbId ?? "");
                  setMikan(item.mikanId?.toString() ?? "");
                }}
              >
                {t("link")}
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={busy === item.id}
                onClick={() =>
                  void act(item.id, async () => {
                    await fetcher(`/api/watchlist/${item.id}`, {
                      method: "DELETE",
                    });
                    await refreshWatchlist();
                  })
                }
              >
                {t("remove")}
              </Button>
            </>
          )}
        </div>
      </div>
      {linking === item.id && (
        <form
          className="mt-3 flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            void act(item.id, async () => {
              await saveWatchlist({
                ...item,
                tmdbId: tmdb || null,
                mikanId: mikan ? Number(mikan) : null,
              });
              setLinking(null);
            });
          }}
        >
          <label className="text-xs">
            {t("tmdb")}
            <input
              className="ml-2 rounded border border-border bg-canvas p-2"
              type="number"
              min="1"
              value={tmdb}
              onChange={(e) => setTmdb(e.target.value)}
            />
          </label>
          <label className="text-xs">
            {t("mikan")}
            <input
              className="ml-2 rounded border border-border bg-canvas p-2"
              type="number"
              min="1"
              value={mikan}
              onChange={(e) => setMikan(e.target.value)}
            />
          </label>
          <Button type="submit" size="sm" disabled={busy === item.id}>
            {t("save")}
          </Button>
          <p className="w-full text-xs text-muted">{t("linkHint")}</p>
        </form>
      )}
      <p className="mt-2 text-xs text-muted">
        {t("expected")}:{" "}
        {item.dayOfWeek === null
          ? t("unknown")
          : `${dayLabel(item.dayOfWeek)} · ${t("timeUnknown")}`}
      </p>
      {!item.tmdbId && (
        <p className="mt-2 text-sm text-muted">{t("unlinked")}</p>
      )}
      <ul className="mt-3 divide-y divide-border-light">
        {item.episodes.map((episode) => (
          <li
            key={`${episode.animationInfoId}:${episode.path ?? ""}`}
            className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm"
          >
            <div>
              <p>
                {episode.season !== null && episode.episode !== null
                  ? `S${episode.season} E${episode.episode}`
                  : episode.title}{" "}
                <span className="ml-2 text-xs text-brand">
                  {t(episode.availability)}
                </span>
              </p>
              <p className="text-xs text-muted">
                {t("actualRelease")}:{" "}
                {new Date(episode.publishedAt).toLocaleString(i18n.language)}
              </p>
            </div>
            {episode.path ? (
              <Link
                className="text-brand"
                to={`/play/${episode.animationInfoId}?file=${encodeURIComponent(episode.path)}`}
              >
                {t(episode.positionSeconds > 0 ? "continue" : "play")}
              </Link>
            ) : (
              item.tmdbId && (
                <Link className="text-brand" to={`/anime/${item.tmdbId}`}>
                  {t("resources")}
                </Link>
              )
            )}
          </li>
        ))}
      </ul>
    </article>
  );
  return (
    <PageTemplate>
      <header className="mb-5">
        <h1 className="font-serif text-2xl">{t("title")}</h1>
        <p className="mt-2 text-sm text-muted">{t("description")}</p>
        <div className="mt-3 flex flex-wrap gap-4 text-sm text-brand">
          <Link to="/">{t("continue")}</Link>
          <Link to="/todo">{t("notifications")}</Link>
          <Link to="/feeds">{t("discover")}</Link>
        </div>
      </header>
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Button size="sm" variant="outline" onClick={() => moveWeek(-1)}>
          {t("previous")}
        </Button>
        <span>
          {dateLabel(week)} –{" "}
          {dateLabel(new Date(week.getTime() + 6 * 86400000))}
        </span>
        <Button size="sm" variant="outline" onClick={() => moveWeek(1)}>
          {t("next")}
        </Button>
        <Button
          size="sm"
          variant="outline"
          onClick={() => setWeek(currentWeek())}
        >
          {t("thisWeek")}
        </Button>
        <select
          aria-label={t("filter")}
          className="rounded border border-border bg-surface p-2 text-sm"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
        >
          <option value="all">{t("all")}</option>
          {watchlistStatuses.map((status) => (
            <option key={status} value={status}>
              {t(`statuses.${status}`)}
            </option>
          ))}
        </select>
      </div>
      {isLoading ? (
        <Spinner />
      ) : error ? (
        <p role="alert">{t("failed")}</p>
      ) : (
        <>
          <p className="mb-3 text-xs text-muted">{t("calendarHint")}</p>
          <div className="mb-6 grid grid-cols-2 gap-2 md:grid-cols-4 lg:grid-cols-7">
            {[1, 2, 3, 4, 5, 6, 0].map((day) => (
              <section
                key={day}
                className="rounded-lg border border-border bg-surface p-3"
              >
                <h2 className="mb-2 text-sm font-medium">{dayLabel(day)}</h2>
                {calendar
                  .filter((item) => item.dayOfWeek === day)
                  .map((item) => (
                    <p key={item.id} className="mb-2 text-xs">
                      {item.title}
                      <span className="block text-subtle">
                        {t("expected")} · {t("timeUnknown")}
                      </span>
                    </p>
                  ))}
              </section>
            ))}
          </div>
          <div className="space-y-4">
            {visible.length ? (
              visible.map(renderItem)
            ) : (
              <p className="text-muted">{t("empty")}</p>
            )}
          </div>
        </>
      )}
    </PageTemplate>
  );
};
