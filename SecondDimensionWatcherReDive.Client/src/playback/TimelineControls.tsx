import Artplayer from "artplayer";
import React from "react";
import { useTranslation } from "react-i18next";
import useSWR from "swr";

import { useAccess } from "../auth/hooks";
import fetcher from "../auth/httpClient";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";

export type EndingProgressGuard = (
  animationInfoId: string,
  path: string,
  position: number,
  duration: number,
) => boolean;

interface Point {
  kind: "opening" | "ending" | "chapter";
  name: string;
  startSeconds: number;
  endSeconds: number;
  enabled: boolean;
}
interface Timeline {
  durationSeconds: number;
  points: Point[];
  updatedAt: string;
}
interface TimelineContext {
  mediaVersion: string;
  seasonKey: string | null;
  episode: Timeline | null;
  seasonDefault: Timeline | null;
  seasonAccepted: boolean;
}
const stamp = (value: number) =>
  `${Math.floor(value / 60)}:${Math.floor(value % 60)
    .toString()
    .padStart(2, "0")}`;

export const TimelineControls: React.FC<{
  animationInfoId: string;
  path: string;
  playerRef: React.RefObject<Artplayer | null>;
  autoSkip: boolean;
  onAutoSkipChange: (enabled: boolean) => void;
  onSkipEnding: () => void;
  endingProgressGuardRef: React.RefObject<EndingProgressGuard | null>;
}> = ({
  animationInfoId,
  path,
  playerRef,
  autoSkip,
  onAutoSkipChange,
  onSkipEnding,
  endingProgressGuardRef,
}) => {
  const { t } = useTranslation("player");
  const { canContentWrite, canPlaybackWrite } = useAccess();
  const { addToast } = useToast();
  const query = new URLSearchParams({ animationInfoId, path });
  const { data, error, mutate } = useSWR<TimelineContext>(
    `/api/playback/timeline?${query}`,
    fetcher,
  );
  const [clock, setClock] = React.useState({ time: 0, duration: 0 });
  const [editing, setEditing] = React.useState(false);
  const [scope, setScope] = React.useState<"episode" | "season">("episode");
  const [points, setPoints] = React.useState<Point[]>([]);
  const [busy, setBusy] = React.useState(false);
  const [validation, setValidation] = React.useState(false);
  const effective =
    data?.episode ?? (data?.seasonAccepted ? data.seasonDefault : null);
  const effectiveRef = React.useRef(effective);
  effectiveRef.current = effective;
  const autoSkipRef = React.useRef(autoSkip);
  autoSkipRef.current = autoSkip;
  const skipEndingRef = React.useRef(onSkipEnding);
  skipEndingRef.current = onSkipEnding;
  React.useLayoutEffect(() => {
    const guard: EndingProgressGuard = (id, mediaPath, position, duration) =>
      id === animationInfoId &&
      mediaPath === path &&
      Boolean(
        effectiveRef.current?.points.some(
          (point) =>
            point.enabled &&
            point.kind === "ending" &&
            point.startSeconds <= position &&
            position < point.endSeconds &&
            point.endSeconds <= duration,
        ),
      );
    endingProgressGuardRef.current = guard;
    return () => {
      if (endingProgressGuardRef.current === guard)
        endingProgressGuardRef.current = null;
    };
  }, [animationInfoId, path, endingProgressGuardRef]);
  const skip = React.useCallback(
    (point: Point) => {
      const art = playerRef.current;
      if (
        !art ||
        point.endSeconds > art.duration ||
        point.endSeconds <= art.currentTime
      )
        return;
      if (point.kind === "ending") skipEndingRef.current();
      art.currentTime = Math.min(point.endSeconds, art.duration);
    },
    [playerRef],
  );
  React.useEffect(() => {
    const timer = window.setInterval(() => {
      const art = playerRef.current;
      if (!art) return;
      const duration = Number.isFinite(art.duration) ? art.duration : 0;
      const time = Number.isFinite(art.currentTime) ? art.currentTime : 0;
      setClock((old) =>
        old.time === time && old.duration === duration
          ? old
          : { time, duration },
      );
      if (!autoSkipRef.current || !art.playing) return;
      const point = effectiveRef.current?.points.find(
        (x) =>
          x.enabled &&
          x.kind !== "chapter" &&
          x.startSeconds <= time &&
          time < x.endSeconds &&
          x.endSeconds <= duration,
      );
      if (point) skip(point);
    }, 250);
    return () => window.clearInterval(timer);
  }, [playerRef, skip]);
  React.useEffect(() => {
    setEditing(false);
    setPoints([]);
    setClock({ time: 0, duration: 0 });
  }, [animationInfoId, path]);
  const active = effective?.points.find(
    (point) =>
      point.enabled &&
      point.kind !== "chapter" &&
      point.startSeconds <= clock.time &&
      clock.time < point.endSeconds &&
      point.endSeconds <= clock.duration,
  );
  const edit = (next: "episode" | "season") => {
    setScope(next);
    setPoints([
      ...(next === "season"
        ? (data?.seasonDefault?.points ?? [])
        : (effective?.points ?? [])),
    ]);
    setEditing(true);
    setValidation(false);
  };
  const act = async (callback: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await callback();
      await mutate();
      setEditing(false);
    } catch {
      addToast({ title: t("timeline.failed"), color: "danger" });
    } finally {
      setBusy(false);
    }
  };
  const update = (index: number, patch: Partial<Point>) =>
    setPoints((old) =>
      old.map((point, position) =>
        position === index ? { ...point, ...patch } : point,
      ),
    );
  const save = () => {
    if (!data || clock.duration <= 0) return;
    const invalid = points.some(
      (point) =>
        !point.name.trim() ||
        !Number.isFinite(point.startSeconds) ||
        !Number.isFinite(point.endSeconds) ||
        point.startSeconds < 0 ||
        point.startSeconds >= clock.duration ||
        point.endSeconds > clock.duration ||
        point.endSeconds < point.startSeconds ||
        (point.kind !== "chapter" && point.endSeconds === point.startSeconds),
    );
    const enabled = points
      .filter((point) => point.enabled && point.kind !== "chapter")
      .sort((a, b) => a.startSeconds - b.startSeconds);
    if (
      invalid ||
      enabled.some(
        (point, index) =>
          index > 0 && enabled[index - 1].endSeconds > point.startSeconds,
      ) ||
      new Set(enabled.map((point) => point.kind)).size !== enabled.length
    ) {
      setValidation(true);
      return;
    }
    setValidation(false);
    void act(() =>
      fetcher("/api/playback/timeline", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          animationInfoId,
          path,
          mediaVersion: data.mediaVersion,
          durationSeconds: clock.duration,
          seasonDefault: scope === "season",
          points,
        }),
      }),
    );
  };
  return (
    <section className="mt-4 rounded-xl border border-border bg-surface p-4 shadow-ring">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-serif text-base">{t("timeline.title")}</h2>
        {active && (
          <Button onClick={() => skip(active)}>
            {t(`timeline.skip.${active.kind}`)}
          </Button>
        )}
      </div>
      <p className="mt-2 text-xs text-muted">{t("timeline.versionHint")}</p>
      {error && (
        <p role="alert" className="mt-2 text-sm text-error">
          {t("timeline.failed")}
        </p>
      )}
      <label className="mt-3 flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          className="accent-brand"
          checked={autoSkip}
          disabled={!canPlaybackWrite}
          onChange={(e) => onAutoSkipChange(e.target.checked)}
        />
        {t("timeline.autoSkip")}
      </label>
      {effective && (
        <p className="mt-2 text-xs text-subtle">
          {t(
            data?.episode ? "timeline.episodeActive" : "timeline.seasonActive",
          )}
        </p>
      )}
      <ol className="mt-3 flex flex-wrap gap-2">
        {effective?.points
          .filter(
            (point) =>
              point.enabled &&
              point.kind === "chapter" &&
              point.startSeconds < clock.duration,
          )
          .sort((a, b) => a.startSeconds - b.startSeconds)
          .map((point, index) => (
            <li key={index}>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  if (playerRef.current)
                    playerRef.current.currentTime = point.startSeconds;
                }}
              >
                {stamp(point.startSeconds)} · {point.name}
              </Button>
            </li>
          ))}
      </ol>
      {canContentWrite && data && (
        <div className="mt-3 flex flex-wrap gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={busy || clock.duration <= 0}
            onClick={() => edit("episode")}
          >
            {t("timeline.editEpisode")}
          </Button>
          {data.seasonKey && (
            <Button
              size="sm"
              variant="outline"
              disabled={busy || clock.duration <= 0}
              onClick={() => edit("season")}
            >
              {t("timeline.editSeason")}
            </Button>
          )}
          {data.seasonDefault && !data.seasonAccepted && (
            <Button
              size="sm"
              variant="outline"
              disabled={busy || clock.duration <= 0}
              onClick={() =>
                void act(() =>
                  fetcher("/api/playback/timeline/accept-season", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                      animationInfoId,
                      path,
                      mediaVersion: data.mediaVersion,
                      durationSeconds: clock.duration,
                    }),
                  }),
                )
              }
            >
              {t("timeline.acceptSeason")}
            </Button>
          )}
          {data.episode && (
            <Button
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() =>
                void act(() =>
                  fetcher(
                    `/api/playback/timeline?${query}&mediaVersion=${data.mediaVersion}&seasonDefault=false`,
                    { method: "DELETE" },
                  ),
                )
              }
            >
              {t("timeline.deleteEpisode")}
            </Button>
          )}
        </div>
      )}
      {data?.seasonDefault && !data.seasonAccepted && (
        <p className="mt-2 text-xs text-warning">
          {t("timeline.confirmHint")}{" "}
          {data.seasonDefault.points
            .map(
              (p) =>
                `${p.name} ${stamp(p.startSeconds)}–${stamp(p.endSeconds)}`,
            )
            .join(" · ")}
        </p>
      )}
      {editing && (
        <form
          className="mt-4 border-t border-border-light pt-4"
          onSubmit={(e) => {
            e.preventDefault();
            save();
          }}
        >
          <h3 className="mb-3 font-medium">
            {t(
              scope === "season"
                ? "timeline.editSeason"
                : "timeline.editEpisode",
            )}{" "}
            · {stamp(clock.duration)}
          </h3>
          <div className="space-y-3">
            {points.map((point, index) => (
              <fieldset
                key={index}
                className="flex flex-wrap items-end gap-2 rounded-lg border border-border-light p-3"
              >
                <legend className="text-xs">
                  {t(`timeline.kinds.${point.kind}`)}
                </legend>
                <label className="text-xs">
                  {t("timeline.name")}
                  <input
                    required
                    maxLength={128}
                    className="mt-1 block rounded border border-border bg-canvas p-2"
                    value={point.name}
                    onChange={(e) => update(index, { name: e.target.value })}
                  />
                </label>
                <label className="text-xs">
                  {t("timeline.start")}
                  <input
                    required
                    type="number"
                    min={0}
                    max={clock.duration}
                    step="0.1"
                    className="mt-1 block w-24 rounded border border-border bg-canvas p-2"
                    value={point.startSeconds}
                    onChange={(e) =>
                      update(index, {
                        startSeconds: Number(e.target.value),
                        ...(point.kind === "chapter"
                          ? { endSeconds: Number(e.target.value) }
                          : {}),
                      })
                    }
                  />
                </label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    update(index, {
                      startSeconds: Math.floor(clock.time),
                      ...(point.kind === "chapter"
                        ? { endSeconds: Math.floor(clock.time) }
                        : {}),
                    })
                  }
                >
                  {t("timeline.setNow")}
                </Button>
                {point.kind !== "chapter" && (
                  <>
                    <label className="text-xs">
                      {t("timeline.end")}
                      <input
                        required
                        type="number"
                        min={0}
                        max={clock.duration}
                        step="0.1"
                        className="mt-1 block w-24 rounded border border-border bg-canvas p-2"
                        value={point.endSeconds}
                        onChange={(e) =>
                          update(index, { endSeconds: Number(e.target.value) })
                        }
                      />
                    </label>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        update(index, { endSeconds: Math.floor(clock.time) })
                      }
                    >
                      {t("timeline.setNow")}
                    </Button>
                  </>
                )}
                <label className="flex items-center gap-1 text-xs">
                  <input
                    type="checkbox"
                    checked={point.enabled}
                    onChange={(e) =>
                      update(index, { enabled: e.target.checked })
                    }
                  />
                  {t("timeline.enabled")}
                </label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setPoints((old) =>
                      old.filter((_, position) => position !== index),
                    )
                  }
                >
                  {t("timeline.remove")}
                </Button>
              </fieldset>
            ))}
          </div>
          {validation && (
            <p role="alert" className="mt-2 text-sm text-error">
              {t("timeline.invalid")}
            </p>
          )}
          <div className="mt-3 flex flex-wrap gap-2">
            {(["opening", "ending", "chapter"] as const).map((kind) => (
              <Button
                key={kind}
                type="button"
                size="sm"
                variant="outline"
                disabled={points.length >= 200}
                onClick={() =>
                  setPoints((old) => [
                    ...old,
                    {
                      kind,
                      name: t(`timeline.kinds.${kind}`),
                      startSeconds: Math.floor(clock.time),
                      endSeconds:
                        kind === "chapter"
                          ? Math.floor(clock.time)
                          : Math.min(
                              clock.duration,
                              Math.floor(clock.time) + 90,
                            ),
                      enabled: true,
                    },
                  ])
                }
              >
                {t("timeline.add", { kind: t(`timeline.kinds.${kind}`) })}
              </Button>
            ))}
          </div>
          <div className="mt-3 flex flex-wrap gap-2">
            <Button type="submit" size="sm" disabled={busy}>
              {t("timeline.save")}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() => setEditing(false)}
            >
              {t("timeline.cancel")}
            </Button>
            {scope === "season" && data?.seasonDefault && (
              <Button
                type="button"
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() =>
                  void act(() =>
                    fetcher(
                      `/api/playback/timeline?${query}&mediaVersion=${data.mediaVersion}&seasonDefault=true`,
                      { method: "DELETE" },
                    ),
                  )
                }
              >
                {t("timeline.deleteSeason")}
              </Button>
            )}
          </div>
        </form>
      )}
    </section>
  );
};
