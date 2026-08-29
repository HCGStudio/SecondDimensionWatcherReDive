import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";

import { Play } from "lucide-react";

import { tmdbImageUrl } from "../animation/tmdbImage";
import { useContinueWatching } from "../playback/hooks";
import { playbackPercent } from "../playback/types";
import { ResilientPoster } from "./ResilientPoster";

const formatPlaybackTime = (seconds: number): string => {
  const value = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(value / 3600);
  const minutes = Math.floor((value % 3600) / 60);
  const remaining = value % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(remaining).padStart(2, "0")}`
    : `${minutes}:${String(remaining).padStart(2, "0")}`;
};

export const ContinueWatching: React.FC = () => {
  const { t } = useTranslation("player");
  const navigate = useNavigate();
  const { data } = useContinueWatching(12);

  if (!data || data.length === 0) return null;

  return (
    <section className="mb-10" aria-labelledby="continue-watching-title">
      <div className="mb-4 flex items-end justify-between gap-3">
        <div>
          <h2
            id="continue-watching-title"
            className="font-serif text-xl font-medium text-foreground"
          >
            {t("continue.title")}
          </h2>
          <p className="mt-1 text-sm text-muted">{t("continue.subtitle")}</p>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {data.map(({ media, state }) => {
          const posterUrl = tmdbImageUrl(media.posterPath, "w300");
          const percent = playbackPercent(
            state.positionSeconds,
            state.durationSeconds,
          );
          const episode = [
            media.season != null
              ? `S${String(media.season).padStart(2, "0")}`
              : null,
            media.episode != null
              ? `E${String(media.episode).padStart(2, "0")}`
              : null,
          ]
            .filter(Boolean)
            .join("");

          return (
            <button
              key={`${media.animationInfoId}:${media.virtualPath}`}
              type="button"
              onClick={() => {
                const params = new URLSearchParams({ file: media.path });
                navigate(`/play/${media.animationInfoId}?${params.toString()}`);
              }}
              className="group overflow-hidden rounded-xl border border-border bg-surface text-left shadow-ring transition-all hover:border-accent/30 hover:shadow-ring-brand"
            >
              <div className="flex min-w-0 gap-3 p-3">
                <ResilientPoster
                  src={posterUrl}
                  alt=""
                  className="h-24 w-16 rounded-md"
                  allowManualRetry={false}
                />
                <div className="flex min-w-0 flex-1 flex-col justify-between py-0.5">
                  <div>
                    <p className="line-clamp-1 text-xs text-accent">
                      {media.animationName ?? t("unknownAnime")}
                      {episode ? ` · ${episode}` : ""}
                    </p>
                    <h3 className="mt-1 line-clamp-2 font-serif text-sm font-medium leading-heading text-foreground transition-colors group-hover:text-accent">
                      {media.title}
                    </h3>
                  </div>
                  <div className="flex items-center justify-between gap-2 text-xs text-subtle">
                    <span>
                      {formatPlaybackTime(state.positionSeconds)} /{" "}
                      {formatPlaybackTime(state.durationSeconds)}
                    </span>
                    <span className="inline-flex items-center gap-1 font-medium text-accent">
                      <Play size={12} fill="currentColor" />
                      {t("continue.resume")}
                    </span>
                  </div>
                </div>
              </div>
              <div
                className="h-1 bg-canvas"
                role="progressbar"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={Math.round(percent)}
              >
                <div
                  className="h-full bg-brand transition-[width]"
                  style={{ width: `${percent}%` }}
                />
              </div>
            </button>
          );
        })}
      </div>
    </section>
  );
};
