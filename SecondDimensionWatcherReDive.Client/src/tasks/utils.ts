import fetcher from "../auth/httpClient";

export const runTask = async (name: string) => {
  return await fetcher(`/api/tasks/${name}/run`, { method: "POST" });
};
