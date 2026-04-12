import fetcher from "../auth/httpClient";

export const refreshSeason = async () => {
  return await fetcher("/api/season/refresh", { method: "POST" });
};

export const subscribeBangumi = async (
  mikanId: number,
  subgroupId?: number,
) => {
  return await fetcher("/api/season/subscribe", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ mikanId, subgroupId: subgroupId ?? null }),
  });
};
