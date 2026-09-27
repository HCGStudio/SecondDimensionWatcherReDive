import { mutate } from "swr";

import fetcher, { authenticatedFetch } from "../auth/httpClient";

export const submitDownload = async (id: string, fromAutomation = false) => {
  return await fetcher(
    `/api/animationinfo/download/${id}${fromAutomation ? "?fromAutomation=true" : ""}`,
    { method: "POST" },
  );
};

export const resumeDownload = async (id: string) => {
  return await fetcher(`/api/animationinfo/resume/${id}`, { method: "POST" });
};

export const pauseDownload = async (id: string) => {
  return await fetcher(`/api/animationinfo/pause/${id}`, { method: "POST" });
};

export const cancelDownload = async (id: string, removeFile = false) => {
  const response = await authenticatedFetch(
    `/api/animationinfo/cancel/${id}?removeFile=${removeFile}`,
    { method: "DELETE" },
  );
  await Promise.allSettled([
    mutate(`/api/animationinfo/status/${id}`, undefined, { revalidate: false }),
    mutate(
      (key) =>
        typeof key === "string" &&
        key.startsWith("/api/animationinfo") &&
        !key.startsWith("/api/animationinfo/status/"),
    ),
  ]);
  return { pending: response.status === 202 };
};

export const retryInference = async (id: string) => {
  return await fetcher(`/api/animationinfo/${id}/retry-inference`, {
    method: "POST",
  });
};

export const reidentifyFilesWithAi = async (id: string) => {
  return await fetcher(`/api/animationinfo/${id}/reidentify-files/ai`, {
    method: "POST",
  });
};
