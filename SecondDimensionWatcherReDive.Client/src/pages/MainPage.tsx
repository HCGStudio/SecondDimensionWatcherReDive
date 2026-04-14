import { AlertTriangle, ArrowLeft, Clapperboard, Film } from "lucide-react";
import React from "react";
import { useNavigate, useParams } from "react-router";

import { useGroupedAnimations } from "../animation/hooks";
import { IAnimationWithEpisodes } from "../animation/IAnimationGrouped";
import { tmdbImageUrl } from "../animation/tmdbImage";
import { AnimationInfo } from "../components/AnimationInfo";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { PageTemplate } from "./PageTemplate";

const AnimeCard: React.FC<{
  anime: IAnimationWithEpisodes;
  onClick: () => void;
}> = ({ anime, onClick }) => {
  const posterUrl = tmdbImageUrl(anime.posterPath, "w300");

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
        <p className="text-xs text-muted">
          {anime.episodeCount} 个剧集
        </p>
      </div>
    </button>
  );
};

export const EpisodeListPage: React.FC = () => {
  const { tmdbId } = useParams<{ tmdbId: string }>();
  const navigate = useNavigate();
  const { data, error } = useGroupedAnimations();

  if (error) {
    return (
      <PageTemplate>
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>加载失败</h2>}
          body={<p>无法获取数据，请稍后重试</p>}
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
          title={<h2>未找到该动画</h2>}
          body={<p>该动画可能已被移除或尚未收录</p>}
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
        返回
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
            {anime.episodeCount} 个剧集
          </p>
        </div>
      </div>

      <div>
        {anime.episodes.map((ep) => (
          <AnimationInfo value={ep} key={ep.id} />
        ))}
      </div>
    </PageTemplate>
  );
};

export const MainPage: React.FC = () => {
  const navigate = useNavigate();
  const { data, error } = useGroupedAnimations();

  if (error) {
    return (
      <PageTemplate>
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>加载失败</h2>}
          body={<p>无法获取数据，请稍后重试</p>}
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
            动画
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
            未分类
          </h2>
          {data.uncategorized.map((item) => (
            <AnimationInfo value={item} key={item.id} />
          ))}
        </div>
      ) : null}

      {data.animations.length === 0 && data.uncategorized.length === 0 ? (
        <EmptyPrompt
          icon={<Clapperboard size={48} />}
          title={<h2>暂无动画</h2>}
          body={<p>订阅 RSS 源后，动画将在这里显示</p>}
        />
      ) : null}
    </PageTemplate>
  );
};
