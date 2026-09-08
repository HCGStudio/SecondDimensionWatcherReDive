import React from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams } from "react-router";

import {
  AlertTriangle,
  ArrowLeft,
  ArrowUpRight,
  Bell,
  ChevronRight,
  Clapperboard,
} from "lucide-react";

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
import { WorkbenchOverview } from "../components/WorkbenchOverview";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { PageTemplate } from "./PageTemplate";

const AnimeRow: React.FC<{ anime: IAnimationCatalogItem }> = ({ anime }) => {
  const { t } = useTranslation("animation");

  return (
    <Link
      to={`/anime/${anime.tmdbId}`}
      className="group flex min-w-0 items-center gap-3 border-b border-border-light py-4 text-left transition-colors hover:bg-tint/50 focus:outline-hidden focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-focus sm:gap-4"
    >
      <ResilientPoster
        src={tmdbImageUrl(anime.posterPath, "w300")}
        alt=""
        className="h-[72px] w-[50px] shrink-0 rounded-md"
        allowManualRetry={false}
      />
      <div className="min-w-0 flex-1">
        <h3 className="line-clamp-2 text-sm font-semibold leading-heading text-foreground transition-colors group-hover:text-accent">
          {anime.name}
        </h3>
        {anime.originalName && anime.originalName !== anime.name ? (
          <p className="mt-1.5 truncate text-xs text-subtle">
            {anime.originalName}
          </p>
        ) : null}
        <p className="mt-1.5 text-xs text-muted sm:hidden">
          {t("episodeSummary", {
            count: anime.episodeCount,
            episodeCount: anime.episodeCount,
            releaseCount: anime.releaseCount,
          })}
        </p>
        {anime.automationAttentionCount > 0 ? (
          <p className="mt-1.5 inline-flex items-center gap-1 text-xs text-warning">
            <Bell size={12} aria-hidden="true" />
            {t("automationAttention", {
              count: anime.automationAttentionCount,
            })}
          </p>
        ) : null}
      </div>
      <div className="hidden w-24 shrink-0 text-right sm:block">
        <p className="text-xs font-medium text-foreground">
          {t("episodeCount", { count: anime.episodeCount })}
        </p>
        <p className="mt-1.5 text-xs text-subtle">
          {t("workbench.releaseCount", { count: anime.releaseCount })}
        </p>
      </div>
      <span className="grid size-8 shrink-0 place-items-center rounded-full border border-border text-brand transition-colors group-hover:border-brand/30 group-hover:bg-tint">
        <ChevronRight size={15} aria-hidden="true" />
      </span>
    </Link>
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
  const { t, i18n } = useTranslation(["animation", "errors"]);
  const catalog = useAnimationCatalog();
  const uncategorized = useUncategorizedAnimations();
  const data = catalog.data;
  const uncategorizedData = uncategorized.data;
  const error = catalog.error ?? uncategorized.error;
  const items = data?.flatMap((page) => page.items) ?? [];
  const uncategorizedItems =
    uncategorizedData?.flatMap((page) => page.items) ?? [];

  return (
    <PageTemplate>
      <div className="mb-8 flex items-end justify-between gap-5">
        <div>
          <p className="mb-2 text-xs font-medium tracking-widest text-subtle">
            {t("animation:workbench.eyebrow")}
          </p>
          <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-[28px]">
            {t("animation:workbench.title")}
          </h1>
          <p className="mt-2 text-sm leading-body text-muted">
            {t("animation:workbench.subtitle")}
          </p>
        </div>
        <time
          className="hidden shrink-0 pb-1 text-xs text-subtle xl:block"
          dateTime={new Date().toISOString()}
        >
          {new Date().toLocaleDateString(i18n.language, {
            month: "long",
            day: "numeric",
            weekday: "long",
          })}
        </time>
      </div>

      <div className="grid min-w-0 gap-8 xl:grid-cols-[minmax(0,1fr)_260px] 2xl:gap-10">
        <div className="min-w-0">
          <ContinueWatching />
          <section aria-labelledby="catalog-title">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
              <h2
                id="catalog-title"
                className="text-base font-semibold text-foreground"
              >
                {t("animation:workbench.library")}
              </h2>
              <Link
                to="/downloaded"
                className="inline-flex items-center gap-1 text-xs font-medium text-brand hover:underline focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus"
              >
                {t("animation:workbench.browseDownloaded")}
                <ArrowUpRight size={14} aria-hidden="true" />
              </Link>
            </div>
            {error ? (
              <EmptyPrompt
                role="alert"
                icon={<AlertTriangle size={36} />}
                title={<h3>{t("errors:loadFailed")}</h3>}
                body={<p>{t("errors:fetchFailed")}</p>}
              />
            ) : !data || !uncategorizedData ? (
              <div
                className="flex items-center justify-center gap-2 py-12 text-sm text-muted"
                role="status"
              >
                <Spinner size={18} />
                {t("animation:workbench.loading")}
              </div>
            ) : (
              <>
                {items.length > 0 ? (
                  <>
                    <div
                      className="flex items-center justify-between border-b border-border-light pb-2.5 pt-1 text-[11px] text-subtle"
                      aria-hidden="true"
                    >
                      <span>{t("animation:workbench.columns.title")}</span>
                      <span className="hidden pr-12 sm:block">
                        {t("animation:workbench.columns.resources")}
                      </span>
                    </div>
                    <div>
                      {items.map((anime) => (
                        <AnimeRow key={anime.tmdbId} anime={anime} />
                      ))}
                    </div>
                    <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
                      <p className="text-xs text-subtle">
                        {t("animation:workbench.loadedCount", {
                          count: items.length,
                        })}
                      </p>
                      {data[data.length - 1]?.nextCursor ? (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={catalog.isValidating}
                          onClick={() => void catalog.setSize(catalog.size + 1)}
                        >
                          {t("animation:loadMore")}
                        </Button>
                      ) : null}
                    </div>
                  </>
                ) : null}

                {uncategorizedItems.length > 0 ? (
                  <section
                    className={items.length > 0 ? "mt-9" : ""}
                    aria-labelledby="uncategorized-title"
                  >
                    <h2
                      id="uncategorized-title"
                      className="mb-4 text-base font-semibold text-foreground"
                    >
                      {t("animation:uncategorized")}
                    </h2>
                    {uncategorizedItems.map((item) => (
                      <AnimationInfo value={item} key={item.id} />
                    ))}
                    {uncategorizedData[uncategorizedData.length - 1]
                      ?.nextCursor ? (
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
                  </section>
                ) : null}

                {items.length === 0 && uncategorizedItems.length === 0 ? (
                  <EmptyPrompt
                    icon={<Clapperboard size={36} />}
                    title={<h3>{t("animation:empty.main.title")}</h3>}
                    body={<p>{t("animation:empty.main.body")}</p>}
                  />
                ) : null}
              </>
            )}
          </section>
        </div>
        <WorkbenchOverview />
      </div>
    </PageTemplate>
  );
};
