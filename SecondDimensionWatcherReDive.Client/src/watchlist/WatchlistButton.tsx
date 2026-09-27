import React from "react";
import { useTranslation } from "react-i18next";

import { BookmarkCheck, BookmarkPlus, Check, Loader2 } from "lucide-react";

import { useAccess } from "../auth/hooks";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "../components/ui/DropdownMenu";
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
  const label = item
    ? `${t("status")}: ${t(`statuses.${item.status}`)}`
    : t("add");

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="icon"
          size="sm"
          className={`h-8 w-8 ${item ? "text-brand" : "text-muted"}`}
          aria-label={label}
          title={label}
          aria-busy={busy}
          disabled={busy}
        >
          {busy ? (
            <Loader2 size={16} className="animate-spin" aria-hidden="true" />
          ) : item ? (
            <BookmarkCheck size={16} aria-hidden="true" />
          ) : (
            <BookmarkPlus size={16} aria-hidden="true" />
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" collisionPadding={12}>
        <DropdownMenuRadioGroup
          aria-label={t("status")}
          value={item?.status ?? ""}
          onValueChange={async (value) => {
            if (busy || value === item?.status) return;
            setBusy(true);
            try {
              await saveWatchlist({
                ...props,
                id: item?.id,
                status: value,
              });
            } catch {
              addToast({ title: t("failed"), color: "danger" });
            } finally {
              setBusy(false);
            }
          }}
        >
          {watchlistStatuses.map((status) => (
            <DropdownMenuRadioItem key={status} value={status} disabled={busy}>
              <span className="h-4 w-4 shrink-0 text-brand" aria-hidden="true">
                {item?.status === status ? <Check size={16} /> : null}
              </span>
              {t(`statuses.${status}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
};
