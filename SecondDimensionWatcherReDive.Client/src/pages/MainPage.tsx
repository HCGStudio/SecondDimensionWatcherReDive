import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router";

import { AlertTriangle, ArrowLeft, Bell, Clapperboard } from "lucide-react";

import { IAnimationCatalogItem } from "../animation/IAnimationCatalog";
import {
  useAnimationCatalog,
  useAnimationEpisodes,
  useUncategorizedAnimations,
} from "../animation/hooks";
import { tmdbImageUrl } from "../animation/tmdbImage";
import { AnimationInfo } from "../components/AnimationInfo";
import { ContinueWatching } from "../components/ContinueWatching";
import { EpisodeList } from "../components/EpisodeList";
import { ResilientPoster } from "../components/ResilientPoster";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { PageTemplate } from "./PageTemplate";

const AnimeCard: React.FC<{
  anime: IAnimationCatalogItem;
  onClick: () => void;
}> = ({ anime, onClick }) => {
  const { t } = useTranslation("animation");
  const posterUrl = tmdbImageUrl(anime.posterPath, "w300");

  return (
    <button
      type="button"
      onClick={onClick}
      className="group flex cursor-pointer gap-4 rounded-lg border border-border bg-surface p-4 text-left shadow-ring transition-all hover:border-accent/30 hover:shadow-ring-brand focus:outline-hidden focus:ring-2 focus:ring-focus"
    >
      <ResilientPoster
        src={posterUrl}
        alt={anime.name}
        className="h-28 w-20 rounded-md"
        allowManualRetry={false}
      />
      <div className="flex min-w-0 flex-1 flex-col justify-between py-0.5">
        <div>
          <h3 className="font-serif text-base font-medium leading-heading text-foreground line-clamp-2 group-hover:text-accent transition-colors">
            {anime.name}
          </h3>
          {anime.originalName && anime.originalName !== anime.name ? (
            <p className="mt-1 text-xs leading-body text-subtle line-clamp-1">
              {anime.originalName}
            </p>
          ) : null}
        </div>
        <div className="space-y-1 text-xs">
          <p className="text-muted">
            {t("episodeSummary", {
              count: anime.episodeCount,
              episodeCount: anime.episodeCount,
              releaseCount: anime.releaseCount,
            })}
          </p>
          {anime.automationAttentionCount > 0 ? (
            <p className="inline-flex items-center gap-1 text-warning">
              <Bell size={12} />
              {t("automationAttention", {
                count: anime.automationAttentionCount,
              })}
            </p>
          ) : null}
        </div>
      </div>
    </button>
  );
};

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

      <div className="mb-6 flex gap-5">
        <ResilientPoster
          src={posterUrl}
          alt={anime.name}
          className="h-36 w-24 rounded-md shadow-ring"
        />
        <div className="flex flex-col justify-center">
          <h2 className="font-serif text-xl font-medium leading-heading text-foreground">
            {anime.name}
          </h2>
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

export const MainPage: React.FC = () => {
  const { t } = useTranslation(["animation", "errors"]);
  const navigate = useNavigate();
  const catalog = useAnimationCatalog();
  const uncategorized = useUncategorizedAnimations();
  const data = catalog.data;
  const uncategorizedData = uncategorized.data;
  const error = catalog.error ?? uncategorized.error;

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

  if (!data || !uncategorizedData) {
    return (
      <PageTemplate>
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      </PageTemplate>
    );
  }

  return (
    <PageTemplate>
      <ContinueWatching />
      {data.flatMap((page) => page.items).length > 0 ? (
        <>
          <h2 className="mb-5 font-serif text-xl font-medium text-foreground">
            {t("animation:sectionTitle")}
          </h2>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {data
              .flatMap((page) => page.items)
              .map((anime) => (
                <AnimeCard
                  key={anime.tmdbId}
                  anime={anime}
                  onClick={() => navigate(`/anime/${anime.tmdbId}`)}
                />
              ))}
          </div>
          {data[data.length - 1]?.nextCursor ? (
            <div className="mt-5 flex justify-center">
              <Button
                variant="outline"
                disabled={catalog.isValidating}
                onClick={() => void catalog.setSize(catalog.size + 1)}
              >
                {t("animation:loadMore")}
              </Button>
            </div>
          ) : null}
        </>
      ) : null}

      {uncategorizedData.flatMap((page) => page.items).length > 0 ? (
        <div
          className={
            data.flatMap((page) => page.items).length > 0 ? "mt-10" : ""
          }
        >
          <h2 className="mb-5 font-serif text-xl font-medium text-foreground">
            {t("animation:uncategorized")}
          </h2>
          {uncategorizedData
            .flatMap((page) => page.items)
            .map((item) => (
              <AnimationInfo value={item} key={item.id} />
            ))}
          {uncategorizedData[uncategorizedData.length - 1]?.nextCursor ? (
            <div className="mt-5 flex justify-center">
              <Button
                variant="outline"
                disabled={uncategorized.isValidating}
                onClick={() =>
                  void uncategorized.setSize(uncategorized.size + 1)
                }
              >
                {t("animation:loadMore")}
              </Button>
            </div>
          ) : null}
        </div>
      ) : null}

      {data.flatMap((page) => page.items).length === 0 &&
      uncategorizedData.flatMap((page) => page.items).length === 0 ? (
        <EmptyPrompt
          icon={<Clapperboard size={48} />}
          title={<h2>{t("animation:empty.main.title")}</h2>}
          body={<p>{t("animation:empty.main.body")}</p>}
        />
      ) : null}
    </PageTemplate>
  );
};
