import React from "react";
import { useTranslation } from "react-i18next";
import useSWR from "swr";

import { useAccess } from "../auth/hooks";
import fetcher from "../auth/httpClient";
import { Button } from "../components/ui/Button";
import { Input } from "../components/ui/Input";
import { Spinner } from "../components/ui/Spinner";
import { IFeed } from "../feed/IFeed";
import { CompletionPlanButton } from "../library/CompletionPlanButton";
import {
  ISubscriptionPolicyDraft,
  createEmptySubscriptionPolicy,
} from "../subscriptionPolicy/types";

interface Subscription extends ISubscriptionPolicyDraft {
  id: string;
  name: string;
  tmdbId: string;
  season: number;
  feedIds: string[];
  waitMinutes: number;
}
interface Decision {
  episode: number;
  waitStartedAt: string;
  waitUntil: string;
  selectedReleaseId: string | null;
  selectedTitle: string | null;
  selectedSourceFeedId: string | null;
  outcome: string;
  reason: string;
}
interface Status {
  subscription: Subscription;
  sources: Array<{
    feedId: string;
    name: string;
    priority: number;
    latestPublishedAt: string | null;
    latestTitle: string | null;
    unidentifiedCount: number;
  }>;
  decisions: Decision[];
}
function createSubscriptionId(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (value) =>
    value.toString(16).padStart(2, "0"),
  ).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

const empty = (): Subscription => ({
  ...createEmptySubscriptionPolicy(),
  id: createSubscriptionId(),
  name: "",
  tmdbId: "",
  season: 1,
  feedIds: [],
  waitMinutes: 1440,
});
const listFields = [
  "subtitleGroups",
  "resolutions",
  "codecs",
  "languages",
  "excludedKeywords",
] as const;
const selectStyle =
  "w-full rounded-md border border-border bg-surface p-2 text-sm text-foreground";

export const MultiSourceSubscriptions: React.FC<{ feeds: IFeed[] }> = ({
  feeds,
}) => {
  const { t } = useTranslation("feeds");
  const { canContentWrite } = useAccess();
  const { data, error, mutate } = useSWR<Status[]>(
    "/api/multi-source-subscriptions",
    fetcher,
    { refreshInterval: 60000 },
  );
  const [draft, setDraft] = React.useState<Subscription | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [failed, setFailed] = React.useState(false);
  const [now, setNow] = React.useState(Date.now());
  React.useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(timer);
  }, []);
  const update = (value: Partial<Subscription>) =>
    setDraft((old) => (old ? { ...old, ...value } : old));
  const act = async (action: () => Promise<unknown>, close = false) => {
    setBusy(true);
    setFailed(false);
    try {
      await action();
      if (close) setDraft(null);
      await mutate();
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  };
  const reorder = (index: number, offset: number) => {
    if (!draft) return;
    const next = [...draft.feedIds];
    [next[index], next[index + offset]] = [next[index + offset], next[index]];
    update({ feedIds: next });
  };
  return (
    <section className="my-8 rounded-xl border border-border bg-surface p-5 shadow-whisper">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-serif text-xl">{t("multiSource.title")}</h2>
        {canContentWrite ? (
          <Button size="sm" onClick={() => setDraft(empty())}>
            {t("multiSource.create")}
          </Button>
        ) : null}
      </div>
      <p className="mt-2 max-w-4xl text-sm text-muted">
        {t("multiSource.description")}
      </p>
      <p className="mt-2 text-xs text-subtle">{t("multiSource.precedence")}</p>
      {error || failed ? (
        <p role="alert" className="mt-4 text-error">
          {t("multiSource.error")}
        </p>
      ) : null}
      {!data && !error ? <Spinner className="mt-4" /> : null}
      {draft && canContentWrite ? (
        <form
          className="mt-5 space-y-4 rounded-lg border border-border-light bg-canvas p-4"
          onSubmit={(event) => {
            event.preventDefault();
            const fields = new FormData(event.currentTarget);
            const submitted = { ...draft };
            for (const field of listFields) {
              submitted[field] = String(fields.get(field) ?? "")
                .split(",")
                .map((value) => value.trim())
                .filter(Boolean);
            }
            void act(
              () =>
                fetcher(`/api/multi-source-subscriptions/${draft.id}`, {
                  method: "PUT",
                  headers: { "Content-Type": "application/json" },
                  body: JSON.stringify(submitted),
                }),
              true,
            );
          }}
        >
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="text-sm">
              {t("multiSource.name")}
              <Input
                required
                maxLength={200}
                value={draft.name}
                onChange={(e) => update({ name: e.target.value })}
              />
            </label>
            <label className="text-sm">
              {t("multiSource.tmdb")}
              <Input
                required
                pattern="[0-9]+"
                value={draft.tmdbId}
                onChange={(e) => update({ tmdbId: e.target.value })}
              />
            </label>
            <label className="text-sm">
              {t("multiSource.season")}
              <Input
                required
                type="number"
                min={1}
                max={100}
                value={draft.season}
                onChange={(e) => update({ season: Number(e.target.value) })}
              />
            </label>
          </div>
          <fieldset>
            <legend className="mb-2 text-sm font-medium">
              {t("multiSource.sources")}
            </legend>
            {draft.feedIds.map((id, index) => (
              <div
                className="mb-2 flex flex-wrap items-center gap-2 rounded-md bg-surface p-2"
                key={id}
              >
                <span className="min-w-0 flex-1 text-sm">
                  {index === 0
                    ? t("multiSource.primary")
                    : t("multiSource.fallback", { priority: index })}{" "}
                  ·{" "}
                  {feeds.find((x) => x.id === id)?.name ||
                    feeds.find((x) => x.id === id)?.url ||
                    id}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={index === 0}
                  aria-label={t("multiSource.moveUp")}
                  onClick={() => reorder(index, -1)}
                >
                  ↑
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={index === draft.feedIds.length - 1}
                  aria-label={t("multiSource.moveDown")}
                  onClick={() => reorder(index, 1)}
                >
                  ↓
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() =>
                    update({ feedIds: draft.feedIds.filter((x) => x !== id) })
                  }
                >
                  {t("multiSource.unlink")}
                </Button>
              </div>
            ))}
            <select
              className={selectStyle}
              aria-label={t("multiSource.addSource")}
              value=""
              onChange={(e) => {
                if (e.target.value)
                  update({ feedIds: [...draft.feedIds, e.target.value] });
              }}
            >
              <option value="">{t("multiSource.addSource")}</option>
              {feeds
                .filter(
                  (f) =>
                    !draft.feedIds.includes(f.id) &&
                    !data?.some(
                      (x) =>
                        x.subscription.id !== draft.id &&
                        x.subscription.feedIds.includes(f.id),
                    ),
                )
                .map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name || f.url}
                  </option>
                ))}
            </select>
          </fieldset>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="text-sm">
              {t("multiSource.mode")}
              <select
                className={selectStyle}
                value={draft.mode}
                onChange={(e) =>
                  update({ mode: e.target.value as Subscription["mode"] })
                }
              >
                {["NotifyOnly", "ManualConfirm", "AutoDownload"].map((mode) => (
                  <option key={mode} value={mode}>
                    {t(`multiSource.modes.${mode}`)}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-sm">
              {t("multiSource.wait")}
              <Input
                type="number"
                required
                min={0}
                max={43200}
                value={draft.waitMinutes}
                onChange={(e) =>
                  update({ waitMinutes: Number(e.target.value) })
                }
              />
            </label>
            {listFields.map((field) => (
              <label className="text-sm" key={field}>
                {t(`multiSource.fields.${field}`)}
                <Input
                  key={`${draft.id}-${field}`}
                  name={field}
                  defaultValue={draft[field].join(", ")}
                  onBlur={(e) =>
                    update({
                      [field]: e.target.value
                        .split(",")
                        .map((x) => x.trim())
                        .filter(Boolean),
                    })
                  }
                />
              </label>
            ))}
            {(["minSizeBytes", "maxSizeBytes"] as const).map((field) => (
              <label className="text-sm" key={field}>
                {t(`multiSource.fields.${field}`)}
                <Input
                  type="number"
                  min={0}
                  value={draft[field] ?? ""}
                  onChange={(e) =>
                    update({
                      [field]:
                        e.target.value === "" ? null : Number(e.target.value),
                    })
                  }
                />
              </label>
            ))}
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={draft.enableVersionUpgrade}
              onChange={(e) =>
                update({ enableVersionUpgrade: e.target.checked })
              }
            />
            {t("multiSource.upgrade")}
          </label>
          {draft.enableVersionUpgrade ? (
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="text-sm">
                {t("multiSource.threshold")}
                <Input
                  type="number"
                  min={1}
                  max={1000}
                  required
                  value={draft.minimumUpgradeScore}
                  onChange={(e) =>
                    update({ minimumUpgradeScore: Number(e.target.value) })
                  }
                />
              </label>
              <label className="text-sm">
                {t("multiSource.rollback")}
                <Input
                  type="number"
                  min={1}
                  max={720}
                  required
                  value={draft.upgradeRollbackHours}
                  onChange={(e) =>
                    update({ upgradeRollbackHours: Number(e.target.value) })
                  }
                />
              </label>
            </div>
          ) : null}
          <p className="text-xs text-subtle">
            {t("multiSource.waitExplanation")}
          </p>
          <div className="flex gap-2">
            <Button type="submit" disabled={busy || draft.feedIds.length === 0}>
              {t("multiSource.save")}
            </Button>
            <Button
              variant="outline"
              disabled={busy}
              onClick={() => setDraft(null)}
            >
              {t("multiSource.cancel")}
            </Button>
          </div>
        </form>
      ) : null}
      {data?.length === 0 ? (
        <p className="mt-4 text-sm text-muted">{t("multiSource.empty")}</p>
      ) : null}
      <div className="mt-5 space-y-4">
        {data?.map((status) => (
          <article
            key={status.subscription.id}
            className="rounded-lg border border-border p-4"
          >
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h3 className="font-serif text-lg">
                  {status.subscription.name} · S{status.subscription.season}
                </h3>
                <p className="text-xs text-muted">
                  TMDB {status.subscription.tmdbId} ·{" "}
                  {t(`multiSource.modes.${status.subscription.mode}`)} ·{" "}
                  {t("multiSource.waitValue", {
                    minutes: status.subscription.waitMinutes,
                  })}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <CompletionPlanButton
                  tmdbId={status.subscription.tmdbId}
                  season={status.subscription.season}
                />
                {canContentWrite ? (
                  <>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy}
                      onClick={() => setDraft({ ...status.subscription })}
                    >
                      {t("multiSource.edit")}
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy}
                      onClick={() =>
                        void act(() =>
                          fetcher(
                            `/api/multi-source-subscriptions/${status.subscription.id}/evaluate`,
                            { method: "POST" },
                          ),
                        )
                      }
                    >
                      {t("multiSource.evaluate")}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={busy}
                      onClick={() =>
                        void act(() =>
                          fetcher(
                            `/api/multi-source-subscriptions/${status.subscription.id}`,
                            { method: "DELETE" },
                          ),
                        )
                      }
                    >
                      {t("multiSource.remove")}
                    </Button>
                  </>
                ) : null}
              </div>
            </div>
            <ul className="mt-3 space-y-2">
              {status.sources.map((source) => (
                <li
                  key={source.feedId}
                  className="rounded-md bg-canvas p-3 text-xs"
                >
                  <p className="font-medium">
                    {source.priority === 0
                      ? t("multiSource.primary")
                      : t("multiSource.fallback", {
                          priority: source.priority,
                        })}{" "}
                    · {source.name}
                  </p>
                  <p className="mt-1 break-words text-muted">
                    {source.latestTitle ?? t("multiSource.noReleases")}
                  </p>
                  {source.latestPublishedAt ? (
                    <p className="mt-1 text-subtle">
                      {t("multiSource.published", {
                        date: new Date(
                          source.latestPublishedAt,
                        ).toLocaleString(),
                      })}
                    </p>
                  ) : null}
                  {source.unidentifiedCount ? (
                    <p className="mt-1 text-warning">
                      {t("multiSource.unidentified", {
                        count: source.unidentifiedCount,
                      })}
                    </p>
                  ) : null}
                </li>
              ))}
            </ul>
            <div className="mt-3 space-y-2">
              {status.decisions.map((decision) => (
                <div
                  key={decision.episode}
                  className="rounded-md border border-border-light p-3 text-xs"
                >
                  <p className="font-medium">
                    E{decision.episode} ·{" "}
                    {t(`multiSource.outcomes.${decision.outcome}`, {
                      defaultValue: decision.outcome,
                    })}
                  </p>
                  {canContentWrite &&
                  decision.outcome === "pending_confirmation" ? (
                    <Button
                      size="sm"
                      className="mt-2"
                      disabled={busy}
                      onClick={() =>
                        void act(async () => {
                          const result = await fetcher<{ isSuccess: boolean }>(
                            `/api/multi-source-subscriptions/${status.subscription.id}/episodes/${decision.episode}/confirm`,
                            { method: "POST" },
                          );
                          if (!result.isSuccess)
                            throw new Error("Confirmation submission failed");
                        })
                      }
                    >
                      {t("multiSource.confirm")}
                    </Button>
                  ) : null}
                  <p className="mt-1 text-muted">
                    {t(`multiSource.reasons.${decision.reason}`, {
                      defaultValue: decision.reason,
                    })}
                  </p>
                  <p className="mt-1 text-subtle">
                    {t("multiSource.timer", {
                      start: new Date(decision.waitStartedAt).toLocaleString(),
                      end: new Date(decision.waitUntil).toLocaleString(),
                      remaining: Math.max(
                        0,
                        Math.ceil(
                          (new Date(decision.waitUntil).getTime() - now) /
                            60000,
                        ),
                      ),
                    })}
                  </p>
                  {decision.selectedReleaseId ? (
                    <p className="mt-1 break-all text-subtle">
                      {t("multiSource.selected", {
                        id:
                          decision.selectedTitle ?? decision.selectedReleaseId,
                      })}
                      {decision.selectedSourceFeedId
                        ? ` · ${status.sources.find((source) => source.feedId === decision.selectedSourceFeedId)?.name ?? ""}`
                        : ""}
                    </p>
                  ) : null}
                </div>
              ))}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
};
