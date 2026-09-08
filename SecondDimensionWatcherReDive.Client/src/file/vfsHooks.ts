import useSWR from "swr";

import {
  AuthIdentityChangedError,
  beginAuthBoundRequest,
  default as fetcher,
} from "../auth/httpClient";
import { IVfsEntry } from "./IVfsEntry";

export const useVfsList = (path: string) => {
  return useSWR<IVfsEntry[]>(
    `/api/vfs/list?path=${encodeURIComponent(path)}`,
    fetcher,
  );
};

export async function downloadVfsFile(
  path: string,
  fileName: string,
): Promise<void> {
  const request = beginAuthBoundRequest();
  try {
    const link = await fetcher<{ url: string }>(
      `/api/vfs/download-link?path=${encodeURIComponent(path)}`,
      { method: "POST" },
    );
    if (!request.isCurrent()) throw new AuthIdentityChangedError();
    // HEAD verifies the actual cookie-bound download entrance without buffering media.
    const ready = await fetch(link.url, {
      method: "HEAD",
      credentials: "same-origin",
      signal: request.signal,
    });
    if (!request.isCurrent()) throw new AuthIdentityChangedError();
    if (!ready.ok) throw new Error(`${ready.status}`);
    const a = document.createElement("a");
    a.href = link.url;
    a.download = fileName;
    a.rel = "noreferrer";
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    request.dispose();
  }
}
