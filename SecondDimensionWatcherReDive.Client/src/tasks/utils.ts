import fetcher from "../auth/httpClient";

export const runTask = async (id: string) => {
  return await fetcher(`/api/tasks/${id}/run`, { method: "POST" });
};
