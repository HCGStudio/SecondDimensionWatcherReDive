import React from "react";
import { useTranslation } from "react-i18next";
import useSWR from "swr";

import { useAccess } from "../auth/hooks";
import fetcher, { authenticatedFetch } from "../auth/httpClient";
import { Button } from "./ui/Button";

interface CapacityEntry {
  itemId: string;
  title: string;
  expectedBytes: number | null;
  remainingBytes: number;
  state: string;
  paused: boolean;
  reason: string;
  createdAt: string;
}

export const DownloadCapacityQueue: React.FC = () => {
  const { t } = useTranslation("animation");
  const { canContentWrite } = useAccess();
  const { data, error, mutate } = useSWR<CapacityEntry[]>(
    "/api/download-capacity",
    fetcher,
    { refreshInterval: 5000 },
  );
  const [busy, setBusy] = React.useState<string | null>(null);
  const [failure, setFailure] = React.useState<string | null>(null);
  const waiting = data?.filter((entry) => entry.state !== "Submitted") ?? [];
  const control = async (
    entry: CapacityEntry,
    action: "cancel" | "pause" | "resume",
  ) => {
    setBusy(entry.itemId);
    setFailure(null);
    try {
      await authenticatedFetch(`/api/animationinfo/${action}/${entry.itemId}`, {
        method: action === "cancel" ? "DELETE" : "POST",
      });
      await mutate();
    } catch {
      setFailure(t("capacity.actionFailed"));
    } finally {
      setBusy(null);
    }
  };
  if (error)
    return (
      <p role="alert" className="mb-4 text-error">
        {t("capacity.loadFailed")}
      </p>
    );
  if (waiting.length === 0) return null;
  return (
    <section
      className="mb-8 rounded-lg border border-border bg-surface p-5"
      aria-label={t("capacity.title")}
    >
      <h2 className="font-serif text-xl">{t("capacity.title")}</h2>
      <p className="my-2 text-sm text-muted">{t("capacity.description")}</p>
      {failure && (
        <p role="alert" className="text-error">
          {failure}
        </p>
      )}
      <ul className="divide-y divide-border">
        {waiting.map((entry) => (
          <li key={entry.itemId} className="py-4">
            <p className="font-medium">{entry.title}</p>
            <p className="mt-1 text-sm">
              {t(`capacity.states.${entry.state}`)}
              {entry.paused ? ` · ${t("capacity.paused")}` : ""} ·{" "}
              {entry.expectedBytes === null
                ? t("capacity.unknownSize")
                : t("capacity.size", {
                    size: (entry.expectedBytes / 1024 ** 3).toFixed(2),
                  })}
            </p>
            <p className="mt-1 break-words text-sm text-muted">
              {entry.reason}
            </p>
            {canContentWrite && (
              <div className="mt-2 flex gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={busy !== null}
                  onClick={() =>
                    void control(entry, entry.paused ? "resume" : "pause")
                  }
                >
                  {t(entry.paused ? "capacity.resume" : "capacity.pause")}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  color="danger"
                  disabled={busy !== null}
                  onClick={() => void control(entry, "cancel")}
                >
                  {t("capacity.cancel")}
                </Button>
              </div>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
};
