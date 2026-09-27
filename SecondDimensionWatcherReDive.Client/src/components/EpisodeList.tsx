import React from "react";
import { useTranslation } from "react-i18next";

import { ArrowUpDown, Hash, SearchX, Users, X } from "lucide-react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { AnimationInfo } from "./AnimationInfo";
import { Button } from "./ui/Button";
import { EmptyPrompt } from "./ui/EmptyPrompt";
import { Select, SelectItem } from "./ui/Select";

type EpisodeSort =
  "published-desc" | "published-asc" | "episode-asc" | "episode-desc";

const DEFAULT_EPISODE_SORT: EpisodeSort = "published-desc";
const EPISODE_SORT_STORAGE_KEY = "sdw.episode-list.sort";
const ALL_EPISODES = "__all-episodes__";
const UNKNOWN_EPISODE = "__unknown-episode__";
const ALL_GROUPS = "__all-groups__";
const UNKNOWN_GROUP = "__unknown-group__";

const episodeSortValues: EpisodeSort[] = [
  "published-desc",
  "published-asc",
  "episode-asc",
  "episode-desc",
];

function isEpisodeSort(value: string | null): value is EpisodeSort {
  return value != null && episodeSortValues.includes(value as EpisodeSort);
}

function readStoredEpisodeSort(): EpisodeSort {
  try {
    const value = window.localStorage.getItem(EPISODE_SORT_STORAGE_KEY);
    return isEpisodeSort(value) ? value : DEFAULT_EPISODE_SORT;
  } catch {
    return DEFAULT_EPISODE_SORT;
  }
}

function storeEpisodeSort(value: EpisodeSort): void {
  try {
    window.localStorage.setItem(EPISODE_SORT_STORAGE_KEY, value);
  } catch {
    // Storage can be unavailable in privacy-restricted browser contexts.
  }
}

function episodeKey(value: IAnimationInfo): string {
  if (value.episode == null) return UNKNOWN_EPISODE;
  return `${value.season ?? ""}:${value.episode}`;
}

function formatEpisodeTag(
  season?: number | null,
  episode?: number | null,
): string {
  const s = season != null ? `S${String(season).padStart(2, "0")}` : "";
  const e = episode != null ? `E${String(episode).padStart(2, "0")}` : "";
  return s + e;
}

function compareNullableNumber(
  left: number | null | undefined,
  right: number | null | undefined,
  direction: 1 | -1,
): number {
  if (left == null && right == null) return 0;
  if (left == null) return 1;
  if (right == null) return -1;
  return (left - right) * direction;
}

function compareEpisodeIdentity(
  left: IAnimationInfo,
  right: IAnimationInfo,
  direction: 1 | -1,
): number {
  return (
    compareNullableNumber(left.season, right.season, direction) ||
    compareNullableNumber(left.episode, right.episode, direction)
  );
}

function compareEpisodes(
  left: IAnimationInfo,
  right: IAnimationInfo,
  sort: EpisodeSort,
): number {
  const leftTime = new Date(left.publishTime).getTime();
  const rightTime = new Date(right.publishTime).getTime();

  switch (sort) {
    case "published-asc":
      return leftTime - rightTime || left.id.localeCompare(right.id);
    case "episode-asc":
      return (
        compareEpisodeIdentity(left, right, 1) ||
        rightTime - leftTime ||
        left.id.localeCompare(right.id)
      );
    case "episode-desc":
      return (
        compareEpisodeIdentity(left, right, -1) ||
        rightTime - leftTime ||
        left.id.localeCompare(right.id)
      );
    case "published-desc":
    default:
      return rightTime - leftTime || right.id.localeCompare(left.id);
  }
}

function uniqueKnownEpisodeCount(episodes: IAnimationInfo[]): number {
  return new Set(
    episodes
      .filter((episode) => episode.episode != null)
      .map((episode) => episodeKey(episode)),
  ).size;
}

export const EpisodeCount: React.FC<{ episodes: IAnimationInfo[] }> = ({
  episodes,
}) => {
  const { t } = useTranslation("animation");
  const uniqueCount = uniqueKnownEpisodeCount(episodes);

  if (uniqueCount > 0 && uniqueCount < episodes.length) {
    return (
      <>
        {t("episodeSummary", {
          count: uniqueCount,
          episodeCount: uniqueCount,
          releaseCount: episodes.length,
        })}
      </>
    );
  }

  return <>{t("episodeCount", { count: episodes.length })}</>;
};

interface SelectControlProps {
  label: string;
  icon: React.ReactNode;
  value: string;
  onValueChange: (value: string) => void;
  children: React.ReactNode;
}

const SelectControl: React.FC<SelectControlProps> = ({
  label,
  icon,
  value,
  onValueChange,
  children,
}) => (
  <label className="block min-w-0">
    <span className="text-xs font-medium leading-body text-muted">{label}</span>
    <span className="relative mt-1 block">
      <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-subtle">
        {icon}
      </span>
      <Select
        value={value}
        onValueChange={onValueChange}
        aria-label={label}
        className="pl-9"
      >
        {children}
      </Select>
    </span>
  </label>
);

export const EpisodeList: React.FC<{ episodes: IAnimationInfo[] }> = ({
  episodes,
}) => {
  const { t } = useTranslation("animation");
  const [episodeFilter, setEpisodeFilter] = React.useState(ALL_EPISODES);
  const [groupFilter, setGroupFilter] = React.useState(ALL_GROUPS);
  const [sort, setSort] = React.useState<EpisodeSort>(readStoredEpisodeSort);

  const episodeOptions = React.useMemo(() => {
    const values = new Map<
      string,
      { season?: number | null; episode?: number | null }
    >();

    for (const episode of episodes) {
      const key = episodeKey(episode);
      if (!values.has(key)) {
        values.set(key, {
          season: episode.season,
          episode: episode.episode,
        });
      }
    }

    return [...values.entries()].sort(([, left], [, right]) => {
      if (left.episode == null) return right.episode == null ? 0 : 1;
      if (right.episode == null) return -1;
      return (
        compareNullableNumber(left.season, right.season, 1) ||
        left.episode - right.episode
      );
    });
  }, [episodes]);

  const groupNames = React.useMemo(
    () =>
      [
        ...new Set(
          episodes.flatMap((episode) =>
            episode.group?.name ? [episode.group.name] : [],
          ),
        ),
      ].sort((left, right) => left.localeCompare(right)),
    [episodes],
  );
  const hasUnknownGroup = episodes.some((episode) => !episode.group?.name);

  React.useEffect(() => {
    if (
      episodeFilter !== ALL_EPISODES &&
      !episodeOptions.some(([key]) => key === episodeFilter)
    ) {
      setEpisodeFilter(ALL_EPISODES);
    }
  }, [episodeFilter, episodeOptions]);

  React.useEffect(() => {
    const isUnknownGroupAvailable =
      groupFilter === UNKNOWN_GROUP && hasUnknownGroup;
    const isKnownGroupAvailable = groupNames.includes(groupFilter);

    if (
      groupFilter !== ALL_GROUPS &&
      !isUnknownGroupAvailable &&
      !isKnownGroupAvailable
    ) {
      setGroupFilter(ALL_GROUPS);
    }
  }, [groupFilter, groupNames, hasUnknownGroup]);

  const visibleEpisodes = React.useMemo(
    () =>
      episodes
        .filter((episode) => {
          const matchesEpisode =
            episodeFilter === ALL_EPISODES ||
            episodeKey(episode) === episodeFilter;
          const matchesGroup =
            groupFilter === ALL_GROUPS ||
            (groupFilter === UNKNOWN_GROUP
              ? !episode.group?.name
              : episode.group?.name === groupFilter);
          return matchesEpisode && matchesGroup;
        })
        .sort((left, right) => compareEpisodes(left, right, sort)),
    [episodeFilter, episodes, groupFilter, sort],
  );

  const hasActiveFilters =
    episodeFilter !== ALL_EPISODES || groupFilter !== ALL_GROUPS;

  const clearFilters = React.useCallback(() => {
    setEpisodeFilter(ALL_EPISODES);
    setGroupFilter(ALL_GROUPS);
  }, []);

  const onSortChange = React.useCallback((value: string) => {
    if (!isEpisodeSort(value)) return;
    setSort(value);
    storeEpisodeSort(value);
  }, []);

  return (
    <>
      <section
        aria-label={t("episodeList.controlsLabel")}
        className="mb-4 rounded-lg border border-border bg-surface p-4 shadow-ring"
      >
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <SelectControl
            label={t("episodeList.episodeFilter")}
            icon={<Hash size={15} aria-hidden="true" />}
            value={episodeFilter}
            onValueChange={setEpisodeFilter}
          >
            <SelectItem value={ALL_EPISODES}>
              {t("episodeList.allEpisodes")}
            </SelectItem>
            {episodeOptions.map(([key, value]) => (
              <SelectItem key={key} value={key}>
                {value.episode == null
                  ? t("episodeList.unknownEpisode")
                  : formatEpisodeTag(value.season, value.episode)}
              </SelectItem>
            ))}
          </SelectControl>

          <SelectControl
            label={t("episodeList.groupFilter")}
            icon={<Users size={15} aria-hidden="true" />}
            value={groupFilter}
            onValueChange={setGroupFilter}
          >
            <SelectItem value={ALL_GROUPS}>
              {t("episodeList.allGroups")}
            </SelectItem>
            {groupNames.map((group) => (
              <SelectItem key={group} value={group}>
                {group}
              </SelectItem>
            ))}
            {hasUnknownGroup ? (
              <SelectItem value={UNKNOWN_GROUP}>
                {t("episodeList.unknownGroup")}
              </SelectItem>
            ) : null}
          </SelectControl>

          <SelectControl
            label={t("episodeList.sortLabel")}
            icon={<ArrowUpDown size={15} aria-hidden="true" />}
            value={sort}
            onValueChange={onSortChange}
          >
            <SelectItem value="published-desc">
              {t("episodeList.sortNewest")}
            </SelectItem>
            <SelectItem value="published-asc">
              {t("episodeList.sortOldest")}
            </SelectItem>
            <SelectItem value="episode-asc">
              {t("episodeList.sortEpisodeAscending")}
            </SelectItem>
            <SelectItem value="episode-desc">
              {t("episodeList.sortEpisodeDescending")}
            </SelectItem>
          </SelectControl>
        </div>

        <div className="mt-3 flex min-h-7 flex-wrap items-center justify-between gap-2 border-t border-border pt-3">
          <p className="text-xs text-subtle" aria-live="polite">
            {t("episodeList.resultCount", {
              visible: visibleEpisodes.length,
              total: episodes.length,
            })}
          </p>
          {hasActiveFilters ? (
            <button
              type="button"
              onClick={clearFilters}
              className="inline-flex cursor-pointer items-center gap-1 text-xs font-medium text-accent transition-colors hover:text-brand focus:outline-hidden focus:ring-2 focus:ring-focus"
            >
              <X size={13} aria-hidden="true" />
              {t("episodeList.clearFilters")}
            </button>
          ) : null}
        </div>
      </section>

      {visibleEpisodes.length > 0 ? (
        <div>
          {visibleEpisodes.map((episode) => (
            <AnimationInfo value={episode} key={episode.id} showTimeOfDay />
          ))}
        </div>
      ) : (
        <EmptyPrompt
          className="py-12"
          icon={<SearchX size={40} />}
          title={<h2>{t("episodeList.noMatches.title")}</h2>}
          body={<p>{t("episodeList.noMatches.body")}</p>}
          actions={
            hasActiveFilters ? (
              <Button variant="outline" onClick={clearFilters}>
                {t("episodeList.clearFilters")}
              </Button>
            ) : undefined
          }
        />
      )}
    </>
  );
};
