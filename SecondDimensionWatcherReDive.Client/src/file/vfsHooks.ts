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
    const res = await fetch(`/api/vfs/read?path=${encodeURIComponent(path)}`, {
      headers: { Authorization: `Bearer ${request.auth.token}` },
      signal: request.signal,
    });
    if (!request.isCurrent()) throw new AuthIdentityChangedError();
    if (!res.ok) throw new Error(`${res.status}`);
    const blob = await res.blob();
    if (!request.isCurrent()) throw new AuthIdentityChangedError();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  } finally {
    request.dispose();
  }
}
