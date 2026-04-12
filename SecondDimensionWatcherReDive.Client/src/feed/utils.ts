import fetcher from "../auth/httpClient";

export const addFeed = async (url: string, name?: string) => {
  return await fetcher("/api/feed", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ url, name }),
  });
};

export const removeFeed = async (id: string) => {
  return await fetcher(`/api/feed/${id}`, { method: "DELETE" });
};
