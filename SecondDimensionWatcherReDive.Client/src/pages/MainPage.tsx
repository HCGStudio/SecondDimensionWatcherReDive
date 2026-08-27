import { AlertTriangle, ArrowLeft, Bell, Clapperboard, Film } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router";

import { useGroupedAnimations } from "../animation/hooks";
import { IAnimationWithEpisodes } from "../animation/IAnimationGrouped";
import { tmdbImageUrl } from "../animation/tmdbImage";
import { AnimationInfo } from "../components/AnimationInfo";
import { EpisodeCount, EpisodeList } from "../components/EpisodeList";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { PageTemplate } from "./PageTemplate";

const AnimeCard: React.FC<{
  anime: IAnimationWithEpisodes;
  onClick: () => void;
}> = ({ anime, onClick }) => {
  const { t } = useTranslation("animation");
  const posterUrl = tmdbImageUrl(anime.posterPath, "w300");
  const automationAttentionCount = anime.episodes.filter((episode) =>
    ["Notified", "PendingConfirmation", "AutoDownloadFailed"].includes(
      episode.automationDisposition ?? "",
    ),
  ).length;

  return (
    <button
      onClick={onClick}
      className="group flex gap-4 rounded-lg border border-border bg-surface p-4 shadow-ring text-left transition-all hover:shadow-ring-brand hover:border-accent/30 cursor-pointer"
    >
      {posterUrl ? (
        <img
          src={posterUrl}
          alt={anime.name}
          className="h-28 w-20 shrink-0 rounded-md object-cover bg-canvas"
          loading="lazy"
        />
      ) : (
        <div className="flex h-28 w-20 shrink-0 items-center justify-center rounded-md bg-canvas text-subtle">
          <Film size={24} />
        </div>
      )}
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
            <EpisodeCount episodes={anime.episodes} />
          </p>
          {automationAttentionCount > 0 ? (
            <p className="inline-flex items-center gap-1 text-warning">
              <Bell size={12} />
              {t("automationAttention", { count: automationAttentionCount })}
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
  const { data, error } = useGroupedAnimations();

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

  const anime = data.animations.find((a) => a.tmdbId === tmdbId);

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
        onClick={() => navigate("/")}
        className="mb-6 inline-flex items-center gap-1.5 text-sm text-muted hover:text-foreground transition-colors cursor-pointer"
      >
        <ArrowLeft size={16} />
        {t("animation:back")}
      </button>

      <div className="mb-6 flex gap-5">
        {posterUrl ? (
          <img
            src={posterUrl}
            alt={anime.name}
            className="h-36 w-24 shrink-0 rounded-md object-cover bg-canvas shadow-ring"
            loading="lazy"
          />
        ) : null}
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
            <EpisodeCount episodes={anime.episodes} />
          </p>
        </div>
      </div>

      <EpisodeList key={anime.tmdbId} episodes={anime.episodes} />
    </PageTemplate>
  );
};

export const MainPage: React.FC = () => {
  const { t } = useTranslation(["animation", "errors"]);
  const navigate = useNavigate();
  const { data, error } = useGroupedAnimations();

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

  return (
    <PageTemplate>
      {data.animations.length > 0 ? (
        <>
          <h2 className="mb-5 font-serif text-xl font-medium text-foreground">
            {t("animation:sectionTitle")}
          </h2>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {data.animations.map((anime) => (
              <AnimeCard
                key={anime.tmdbId}
                anime={anime}
                onClick={() => navigate(`/anime/${anime.tmdbId}`)}
              />
            ))}
          </div>
        </>
      ) : null}

      {data.uncategorized.length > 0 ? (
        <div className={data.animations.length > 0 ? "mt-10" : ""}>
          <h2 className="mb-5 font-serif text-xl font-medium text-foreground">
            {t("animation:uncategorized")}
          </h2>
          {data.uncategorized.map((item) => (
            <AnimationInfo value={item} key={item.id} />
          ))}
        </div>
      ) : null}

      {data.animations.length === 0 && data.uncategorized.length === 0 ? (
        <EmptyPrompt
          icon={<Clapperboard size={48} />}
          title={<h2>{t("animation:empty.main.title")}</h2>}
          body={<p>{t("animation:empty.main.body")}</p>}
        />
      ) : null}
    </PageTemplate>
  );
};
