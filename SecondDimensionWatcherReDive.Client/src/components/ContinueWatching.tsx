import React from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";

import { ArrowRight, ChevronDown, Play } from "lucide-react";

import { tmdbImageUrl } from "../animation/tmdbImage";
import { useContinueWatching } from "../playback/hooks";
import { ContinueWatchingItem, playbackPercent } from "../playback/types";
import { preloadPlayerPage } from "../routes/pageLoaders";
import { ResilientPoster } from "./ResilientPoster";
import { Spinner } from "./ui/Spinner";

const formatPlaybackTime = (seconds: number): string => {
  const value = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(value / 3600);
  const minutes = Math.floor((value % 3600) / 60);
  const remaining = value % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(remaining).padStart(2, "0")}`
    : `${minutes}:${String(remaining).padStart(2, "0")}`;
};

const ResumeCard: React.FC<{
  item: ContinueWatchingItem;
  featured?: boolean;
}> = ({ item: { media, state }, featured = false }) => {
  const { t } = useTranslation("player");
  const navigate = useNavigate();
  const percent = playbackPercent(state.positionSeconds, state.durationSeconds);
  const episode = [
    media.season != null ? `S${String(media.season).padStart(2, "0")}` : null,
    media.episode != null ? `E${String(media.episode).padStart(2, "0")}` : null,
  ]
    .filter(Boolean)
    .join("");

  return (
    <button
      type="button"
      onMouseEnter={preloadPlayerPage}
      onFocus={preloadPlayerPage}
      onClick={() => {
        const params = new URLSearchParams({ file: media.path });
        navigate(`/play/${media.animationInfoId}?${params.toString()}`);
      }}
      className={`group relative overflow-hidden rounded-xl text-left transition-colors focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus ${featured ? "w-full bg-tint hover:bg-brand/10" : "w-64 shrink-0 border border-border-light bg-surface hover:border-brand/30"}`}
    >
      <div
        className={`flex min-w-0 items-center ${featured ? "gap-4 p-5 sm:gap-6 sm:p-6" : "gap-3 p-3"}`}
      >
        <ResilientPoster
          src={tmdbImageUrl(media.posterPath, "w300")}
          alt=""
          className={
            featured
              ? "h-[124px] w-[84px] shrink-0 rounded-md shadow-sm sm:h-[138px] sm:w-[94px]"
              : "h-16 w-11 shrink-0 rounded"
          }
          allowManualRetry={false}
        />
        <div className="min-w-0 flex-1">
          {featured ? (
            <p className="mb-2 text-[11px] font-medium tracking-wider text-brand">
              {t("continue.title")}
            </p>
          ) : null}
          <h3
            className={`line-clamp-2 font-semibold leading-heading text-foreground group-hover:text-brand ${featured ? "text-lg sm:text-xl" : "text-xs"}`}
          >
            {media.animationName ?? t("unknownAnime")}
          </h3>
          <p
            className={`line-clamp-1 text-muted ${featured ? "mt-2 text-xs" : "mt-1 text-[11px]"}`}
          >
            {episode ? `${episode} · ` : ""}
            {media.title}
          </p>
          <p
            className={`tabular-nums text-subtle ${featured ? "mt-1.5 text-xs" : "mt-1 text-[11px]"}`}
          >
            {formatPlaybackTime(state.positionSeconds)} /{" "}
            {formatPlaybackTime(state.durationSeconds)}
          </p>
          {featured ? (
            <span className="mt-4 inline-flex items-center gap-2 rounded-md bg-brand px-3 py-2 text-xs font-medium text-surface">
              <Play size={12} fill="currentColor" aria-hidden="true" />
              {t("continue.resume")}
            </span>
          ) : null}
        </div>
        {!featured ? (
          <Play
            className="shrink-0 text-brand"
            size={13}
            fill="currentColor"
            aria-hidden="true"
          />
        ) : null}
      </div>
      <div
        className="h-[3px] bg-brand/10"
        role="progressbar"
        aria-label={t("continue.progress")}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(percent)}
      >
        <div
          className="h-full bg-brand/70 transition-[width]"
          style={{ width: `${percent}%` }}
        />
      </div>
    </button>
  );
};

export const ContinueWatching: React.FC = () => {
  const { t } = useTranslation("player");
  const { data, error, mutate } = useContinueWatching(12);

  return (
    <section className="mb-8" aria-labelledby="continue-watching-title">
      <h2 id="continue-watching-title" className="sr-only">
        {t("continue.title")}
      </h2>
      {error ? (
        <div
          className="rounded-xl border border-border-light bg-tint p-5 text-sm text-muted"
          role="alert"
        >
          <p>{t("continue.loadFailed")}</p>
          <button
            type="button"
            onClick={() => void mutate()}
            className="mt-2 rounded text-xs font-medium text-brand hover:underline focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus"
          >
            {t("retry")}
          </button>
        </div>
      ) : !data ? (
        <div
          className="flex items-center gap-2 rounded-xl bg-tint p-6 text-sm text-muted"
          role="status"
        >
          <Spinner size={18} />
          {t("continue.loading")}
        </div>
      ) : data.length === 0 ? (
        <div className="rounded-xl bg-tint p-6">
          <h3 className="text-base font-semibold text-foreground">
            {t("continue.emptyTitle")}
          </h3>
          <p className="mt-2 text-sm leading-body text-muted">
            {t("continue.emptyBody")}
          </p>
          <Link
            to="/downloaded"
            className="mt-4 inline-flex items-center gap-2 rounded text-xs font-medium text-brand hover:underline focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus"
          >
            {t("continue.browse")}
            <ArrowRight size={14} aria-hidden="true" />
          </Link>
        </div>
      ) : (
        <>
          <ResumeCard item={data[0]} featured />
          {data.length > 1 ? (
            <details className="group/continue mt-2">
              <summary className="flex cursor-pointer list-none items-center gap-2 rounded py-2 text-xs text-muted hover:text-brand focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus [&::-webkit-details-marker]:hidden">
                <ChevronDown
                  size={14}
                  className="transition-transform group-open/continue:rotate-180"
                  aria-hidden="true"
                />
                <span>{t("continue.more")}</span>
                <span className="ml-auto text-[11px] text-subtle">
                  {t("continue.itemCount", { count: data.length - 1 })}
                </span>
              </summary>
              <div
                className="flex gap-3 overflow-x-auto pb-2 pt-1"
                aria-label={t("continue.more")}
              >
                {data.slice(1).map((item) => (
                  <ResumeCard
                    key={`${item.media.animationInfoId}:${item.media.virtualPath}`}
                    item={item}
                  />
                ))}
              </div>
            </details>
          ) : null}
        </>
      )}
    </section>
  );
};
