import fetcher from "../auth/httpClient";
import { IFileLinkResult } from "./IFileLinkResult";

export const generatePlaybackLink = async (
  id: string,
  path?: string,
): Promise<IFileLinkResult> => {
  return await fetcher("/api/file/generateLink", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, path }),
  });
};
