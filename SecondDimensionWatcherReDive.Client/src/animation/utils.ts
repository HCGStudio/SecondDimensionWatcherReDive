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

export const cancelDownload = async (id: string, removeFile = false) => {
  return await fetcher(
    `/api/animationinfo/cancel/${id}?removeFile=${removeFile}`,
    { method: "DELETE" },
  );
};

export const retryInference = async (id: string) => {
  return await fetcher(`/api/animationinfo/${id}/retry-inference`, {
    method: "POST",
  });
};
