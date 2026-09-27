import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router";

import { AlertTriangle, ArrowLeft, Clapperboard } from "lucide-react";

import { useAnimationEpisodes } from "../animation/hooks";
import { tmdbImageUrl } from "../animation/tmdbImage";
import { EpisodeList } from "../components/EpisodeList";
import { ResilientPoster } from "../components/ResilientPoster";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { WatchlistButton } from "../watchlist/WatchlistButton";
import { PageTemplate } from "./PageTemplate";

export const EpisodeListPage: React.FC = () => {
  const { t } = useTranslation(["animation", "errors"]);
  const { tmdbId } = useParams<{ tmdbId: string }>();
  const navigate = useNavigate();
  const { data, error, size, setSize, isValidating } =
    useAnimationEpisodes(tmdbId);

  if (error) {
    return (
      <PageTemplate>
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>{t("errors:loadFailed")}</h2>}
          body={<p>{t("errors:fetchFailed")}</p>}
        />
      </PageTemplate>
    );
  }

  if (!data) {
    return (
      <PageTemplate>
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      </PageTemplate>
    );
  }

  const anime = data[0]?.animation;

  if (!anime) {
    return (
      <PageTemplate>
        <EmptyPrompt
          icon={<Clapperboard size={48} />}
          title={<h2>{t("animation:empty.animeNotFound.title")}</h2>}
          body={<p>{t("animation:empty.animeNotFound.body")}</p>}
        />
      </PageTemplate>
    );
  }

  const posterUrl = tmdbImageUrl(anime.posterPath, "w300");

  return (
    <PageTemplate>
      <button
        type="button"
        onClick={() => navigate("/")}
        className="mb-6 inline-flex cursor-pointer items-center gap-1.5 rounded-md py-1 text-sm text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
      >
        <ArrowLeft size={16} />
        {t("animation:back")}
      </button>

      <div className="mb-7 flex gap-5 rounded-xl border border-border-light bg-tint p-5 sm:p-6">
        <ResilientPoster
          src={posterUrl}
          alt={anime.name}
          className="h-36 w-24 rounded-md shadow-ring"
        />
        <div className="flex flex-col justify-center">
          <h2 className="text-xl font-semibold leading-heading text-foreground">
            {anime.name}
          </h2>
          <div className="mt-3">
            <WatchlistButton tmdbId={tmdbId} title={anime.name} />
          </div>
          {anime.originalName && anime.originalName !== anime.name ? (
            <p className="mt-1 text-sm leading-body text-subtle">
              {anime.originalName}
            </p>
          ) : null}
          <p className="mt-2 text-sm text-muted">
            {t("animation:episodeSummary", {
              count: anime.episodeCount,
              episodeCount: anime.episodeCount,
              releaseCount: anime.releaseCount,
            })}
          </p>
        </div>
      </div>

      <EpisodeList
        key={anime.tmdbId}
        episodes={data.flatMap((page) => page.episodes)}
      />
      {data[data.length - 1]?.nextCursor ? (
        <div className="mt-5 flex justify-center">
          <Button
            variant="outline"
            disabled={isValidating}
            onClick={() => void setSize(size + 1)}
          >
            {t("animation:loadMore")}
          </Button>
        </div>
      ) : null}
    </PageTemplate>
  );
};
