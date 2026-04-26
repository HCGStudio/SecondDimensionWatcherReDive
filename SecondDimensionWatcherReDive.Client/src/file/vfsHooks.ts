import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IVfsEntry } from "./IVfsEntry";

export const useVfsList = (path: string) => {
  return useSWR<IVfsEntry[]>(
    `/api/vfs/list?path=${encodeURIComponent(path)}`,
    fetcher,
  );
};

function getAuthHeaders(): HeadersInit {
  const authStr = localStorage.getItem("auth");
  if (!authStr) return {};
  try {
    const auth = JSON.parse(authStr);
    return { Authorization: `Bearer ${auth.token}` };
  } catch {
    return {};
  }
}

export async function downloadVfsFile(
  path: string,
  fileName: string,
): Promise<void> {
  const res = await fetch(`/api/vfs/read?path=${encodeURIComponent(path)}`, {
    headers: getAuthHeaders(),
  });
  if (!res.ok) throw new Error(`${res.status}`);
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
