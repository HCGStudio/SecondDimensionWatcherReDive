import React from "react";
import { useTranslation } from "react-i18next";

import { useAccess } from "../auth/hooks";
import { useToast } from "../components/ToastProvider";
import { saveWatchlist, useWatchlist, watchlistStatuses } from "./hooks";

export const WatchlistButton: React.FC<{
  tmdbId?: string;
  mikanId?: number;
  title: string;
}> = (props) => {
  const { t } = useTranslation("watchlist");
  const { canPlaybackWrite } = useAccess();
  const { data } = useWatchlist();
  const { addToast } = useToast();
  const [busy, setBusy] = React.useState(false);
  const item = data?.find(
    (x) =>
      (props.tmdbId && x.tmdbId === props.tmdbId) ||
      (props.mikanId && x.mikanId === props.mikanId),
  );
  if (!canPlaybackWrite) return null;
  return (
    <select
      className="max-w-full rounded-md border border-border bg-surface px-2 py-1 text-xs"
      aria-label={t("add")}
      value={item?.status ?? ""}
      disabled={busy}
      onChange={async (event) => {
        setBusy(true);
        try {
          await saveWatchlist({
            ...props,
            id: item?.id,
            status: event.target.value,
          });
        } catch {
          addToast({ title: t("failed"), color: "danger" });
        } finally {
          setBusy(false);
        }
      }}
    >
      <option value="" disabled>
        {t("add")}
      </option>
      {watchlistStatuses.map((status) => (
        <option key={status} value={status}>
          {t(`statuses.${status}`)}
        </option>
      ))}
    </select>
  );
};
