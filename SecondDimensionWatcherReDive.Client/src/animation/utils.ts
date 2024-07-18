import fetcher from "../auth/httpClient";

export const submitDownload = async (id: string) => {
  return await fetcher(`/api/animationinfo/download/${id}`, { method: "POST" });
};

export const resumeDownload = async (id: string) => {
  return await fetcher(`/api/animationinfo/resume/${id}`, { method: "POST" });
};

export const pauseDownload = async (id: string) => {
  return await fetcher(`/api/animationinfo/pause/${id}`, { method: "POST" });
};
