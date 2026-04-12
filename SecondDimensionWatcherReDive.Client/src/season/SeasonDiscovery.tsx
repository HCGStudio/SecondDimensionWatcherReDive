import { Check, ChevronLeft, ChevronRight, RefreshCw, Rss, Users } from "lucide-react";
import React from "react";

import { useFeeds } from "../feed/hooks";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { Spinner } from "../components/ui/Spinner";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetBody,
  SheetTitle,
} from "../components/ui/Sheet";
import { useBangumiSubgroups, useSeasonBangumis } from "./hooks";
import { refreshSeason, subscribeBangumi } from "./utils";
import { ISeasonBangumi, SeasonOption } from "./types";

const DAY_LABELS: Record<number, string> = {
  1: "星期一",
  2: "星期二",
  3: "星期三",
  4: "星期四",
  5: "星期五",
  6: "星期六",
  0: "星期日",
  7: "剧场版",
  8: "OVA",
};

const DAY_ORDER = [1, 2, 3, 4, 5, 6, 0, 7, 8];

const MIKAN_BASE = "https://mikanani.me";

const SEASONS = ["冬", "春", "夏", "秋"] as const;
const SEASON_LABELS: Record<string, string> = {
  "冬": "冬季",
  "春": "春季",
  "夏": "夏季",
  "秋": "秋季",
};

function getCurrentSeason(): { year: number; season: string } {
  const now = new Date();
  const month = now.getMonth() + 1;
  let season: string;
  if (month >= 1 && month <= 3) season = "冬";
  else if (month >= 4 && month <= 6) season = "春";
  else if (month >= 7 && month <= 9) season = "夏";
  else season = "秋";
  return { year: now.getFullYear(), season };
}

function getAdjacentSeason(opt: SeasonOption, delta: number): SeasonOption {
  const idx = SEASONS.indexOf(opt.season as typeof SEASONS[number]);
  const newIdx = idx + delta;
  if (newIdx < 0) {
    return {
      year: opt.year - 1,
      season: SEASONS[SEASONS.length - 1],
      label: `${opt.year - 1} ${SEASON_LABELS[SEASONS[SEASONS.length - 1]]}`,
    };
  }
  if (newIdx >= SEASONS.length) {
    return {
      year: opt.year + 1,
      season: SEASONS[0],
      label: `${opt.year + 1} ${SEASON_LABELS[SEASONS[0]]}`,
    };
  }
  return {
    year: opt.year,
    season: SEASONS[newIdx],
    label: `${opt.year} ${SEASON_LABELS[SEASONS[newIdx]]}`,
  };
}

function buildAllRssUrl(mikanId: number): string {
  return `${MIKAN_BASE}/RSS/Bangumi?bangumiId=${mikanId}`;
}

function buildSubgroupRssUrl(mikanId: number, subgroupId: number): string {
  return `${MIKAN_BASE}/RSS/Bangumi?bangumiId=${mikanId}&subgroupid=${subgroupId}`;
}

export const SeasonDiscovery: React.FC = () => {
  const current = getCurrentSeason();
  const [selectedSeason, setSelectedSeason] = React.useState<SeasonOption>({
    year: current.year,
    season: current.season,
    label: `${current.year} ${SEASON_LABELS[current.season]}`,
  });

  const isCurrent =
    selectedSeason.year === current.year && selectedSeason.season === current.season;

  const {
    data: seasonData,
    error,
    isLoading,
    mutate: mutateSeason,
  } = useSeasonBangumis(
    isCurrent ? undefined : selectedSeason.year,
    isCurrent ? undefined : selectedSeason.season,
  );

  const { data: feeds, mutate: mutateFeeds } = useFeeds();
  const { addToast } = useToast();
  const [refreshing, setRefreshing] = React.useState(false);
  const [selectedBangumi, setSelectedBangumi] = React.useState<ISeasonBangumi | null>(null);

  const subscribedUrls = React.useMemo(() => {
    const set = new Set<string>();
    feeds?.forEach((f) => set.add(f.url));
    return set;
  }, [feeds]);

  const onRefresh = React.useCallback(async () => {
    setRefreshing(true);
    try {
      if (isCurrent) {
        await refreshSeason();
      }
      await mutateSeason();
      addToast({ title: "新番列表已更新", color: "success" });
    } catch {
      addToast({ title: "更新失败，请稍后重试", color: "danger" });
    } finally {
      setRefreshing(false);
    }
  }, [isCurrent, mutateSeason, addToast]);

  const onSubscribeAll = React.useCallback(
    async (bangumi: ISeasonBangumi) => {
      try {
        await subscribeBangumi(bangumi.mikanId);
        await mutateFeeds();
        addToast({ title: `已订阅「${bangumi.title}」`, color: "success" });
      } catch {
        addToast({ title: "订阅失败", color: "danger" });
      }
    },
    [mutateFeeds, addToast],
  );

  const onPrev = React.useCallback(() => {
    setSelectedSeason((s) => getAdjacentSeason(s, -1));
  }, []);

  const onNext = React.useCallback(() => {
    const next = getAdjacentSeason(selectedSeason, 1);
    // Don't go beyond current season
    if (next.year > current.year || (next.year === current.year && SEASONS.indexOf(next.season as typeof SEASONS[number]) > SEASONS.indexOf(current.season as typeof SEASONS[number]))) {
      return;
    }
    setSelectedSeason(next);
  }, [selectedSeason, current]);

  const canGoNext = React.useMemo(() => {
    const next = getAdjacentSeason(selectedSeason, 1);
    if (next.year > current.year) return false;
    if (next.year === current.year && SEASONS.indexOf(next.season as typeof SEASONS[number]) > SEASONS.indexOf(current.season as typeof SEASONS[number])) return false;
    return true;
  }, [selectedSeason, current]);

  // Group bangumis by day
  const grouped = React.useMemo(() => {
    const map = new Map<number, ISeasonBangumi[]>();
    seasonData?.bangumis?.forEach((b) => {
      const list = map.get(b.dayOfWeek) ?? [];
      list.push(b);
      map.set(b.dayOfWeek, list);
    });
    return map;
  }, [seasonData]);

  if (error) return null;

  return (
    <div className="mb-10">
      {/* Header with season selector */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <h2 className="font-serif text-xl font-medium text-foreground">番组列表</h2>
          <div className="flex items-center gap-1">
            <Button variant="icon" size="sm" onClick={onPrev}>
              <ChevronLeft size={16} />
            </Button>
            <span className="min-w-[7rem] text-center text-sm font-medium text-foreground">
              {selectedSeason.label}
            </span>
            <Button variant="icon" size="sm" onClick={onNext} disabled={!canGoNext}>
              <ChevronRight size={16} />
            </Button>
          </div>
        </div>
        <div className="flex items-center gap-3">
          {seasonData?.lastScrapedAt ? (
            <span className="text-xs text-subtle">
              上次更新: {new Date(seasonData.lastScrapedAt).toLocaleString()}
            </span>
          ) : null}
          <Button
            variant="outline"
            size="sm"
            onClick={onRefresh}
            disabled={refreshing || isLoading}
          >
            <RefreshCw size={14} className={refreshing || isLoading ? "animate-spin" : ""} />
            刷新
          </Button>
        </div>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-8"><Spinner /></div>
      ) : seasonData?.bangumis.length === 0 ? (
        <p className="text-sm text-muted">暂无新番数据，请点击刷新获取</p>
      ) : (
        DAY_ORDER.filter((d) => grouped.has(d)).map((day) => (
          <div key={day} className="mb-6">
            <h3 className="mb-3 font-serif text-base font-medium text-muted">
              {DAY_LABELS[day]}
            </h3>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {grouped.get(day)!.map((bangumi) => {
                const allUrl = buildAllRssUrl(bangumi.mikanId);
                const isSubscribed = subscribedUrls.has(allUrl);

                return (
                  <div
                    key={bangumi.mikanId}
                    className="flex gap-3 rounded-md border border-border bg-surface p-3 shadow-whisper"
                  >
                    {bangumi.imageUrl ? (
                      <img
                        src={MIKAN_BASE + bangumi.imageUrl}
                        alt={bangumi.title}
                        className="h-20 w-14 shrink-0 rounded object-cover"
                        loading="lazy"
                      />
                    ) : (
                      <div className="flex h-20 w-14 shrink-0 items-center justify-center rounded bg-canvas text-subtle">
                        <Rss size={20} />
                      </div>
                    )}
                    <div className="flex min-w-0 flex-1 flex-col justify-between">
                      <p className="line-clamp-2 text-sm font-medium leading-snug text-foreground">
                        {bangumi.title}
                      </p>
                      <div className="flex gap-2">
                        <Button
                          size="sm"
                          variant={isSubscribed ? "outline" : "solid"}
                          disabled={isSubscribed}
                          onClick={() => onSubscribeAll(bangumi)}
                        >
                          {isSubscribed ? (
                            <>
                              <Check size={12} />
                              已订阅
                            </>
                          ) : (
                            <>
                              <Rss size={12} />
                              订阅
                            </>
                          )}
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => setSelectedBangumi(bangumi)}
                        >
                          <Users size={12} />
                          字幕组
                        </Button>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        ))
      )}

      <Sheet
        open={selectedBangumi != null}
        onOpenChange={(open) => {
          if (!open) setSelectedBangumi(null);
        }}
      >
        <SheetContent>
          <SheetHeader>
            <SheetTitle>{selectedBangumi?.title ?? ""}</SheetTitle>
            <p className="mt-1 text-sm text-muted">选择字幕组订阅</p>
          </SheetHeader>
          <SheetBody>
            {selectedBangumi ? (
              <SubgroupList
                bangumi={selectedBangumi}
                subscribedUrls={subscribedUrls}
                onSubscribed={mutateFeeds}
              />
            ) : null}
          </SheetBody>
        </SheetContent>
      </Sheet>
    </div>
  );
};

const SubgroupList: React.FC<{
  bangumi: ISeasonBangumi;
  subscribedUrls: Set<string>;
  onSubscribed: () => void;
}> = ({ bangumi, subscribedUrls, onSubscribed }) => {
  const { data: subgroups, error } = useBangumiSubgroups(bangumi.mikanId);
  const { addToast } = useToast();

  const onSubscribe = React.useCallback(
    async (subgroupId: number, name: string) => {
      try {
        await subscribeBangumi(bangumi.mikanId, subgroupId);
        onSubscribed();
        addToast({
          title: `已订阅「${bangumi.title} - ${name}」`,
          color: "success",
        });
      } catch {
        addToast({ title: "订阅失败", color: "danger" });
      }
    },
    [bangumi, onSubscribed, addToast],
  );

  if (error) return <p className="text-sm text-error">加载字幕组失败</p>;
  if (!subgroups) return <div className="flex justify-center py-8"><Spinner /></div>;
  if (subgroups.length === 0) return <p className="text-sm text-muted">暂无字幕组数据</p>;

  return (
    <div className="space-y-3">
      {/* Subscribe to all subgroups */}
      {(() => {
        const allUrl = buildAllRssUrl(bangumi.mikanId);
        const isAllSubscribed = subscribedUrls.has(allUrl);
        return (
          <div className="flex items-center justify-between rounded-md border border-border-light bg-canvas p-3">
            <span className="text-sm font-medium text-foreground">全部字幕组</span>
            <Button
              size="sm"
              variant={isAllSubscribed ? "outline" : "solid"}
              disabled={isAllSubscribed}
              onClick={async () => {
                try {
                  await subscribeBangumi(bangumi.mikanId);
                  onSubscribed();
                  addToast({ title: `已订阅「${bangumi.title}」`, color: "success" });
                } catch {
                  addToast({ title: "订阅失败", color: "danger" });
                }
              }}
            >
              {isAllSubscribed ? (
                <>
                  <Check size={12} />
                  已订阅
                </>
              ) : (
                <>
                  <Rss size={12} />
                  订阅
                </>
              )}
            </Button>
          </div>
        );
      })()}

      {subgroups.map((sg) => {
        const rssUrl = buildSubgroupRssUrl(bangumi.mikanId, sg.mikanSubgroupId);
        const isSubscribed = subscribedUrls.has(rssUrl);

        return (
          <div
            key={sg.mikanSubgroupId}
            className="flex items-center justify-between rounded-md border border-border-light p-3"
          >
            <span className="text-sm text-foreground">{sg.name}</span>
            <Button
              size="sm"
              variant={isSubscribed ? "outline" : "solid"}
              disabled={isSubscribed}
              onClick={() => onSubscribe(sg.mikanSubgroupId, sg.name)}
            >
              {isSubscribed ? (
                <>
                  <Check size={12} />
                  已订阅
                </>
              ) : (
                <>
                  <Rss size={12} />
                  订阅
                </>
              )}
            </Button>
          </div>
        );
      })}
    </div>
  );
};
