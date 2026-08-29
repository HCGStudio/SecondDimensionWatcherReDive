import fetcher from "../auth/httpClient";

const API_BASE = "/api/chat";

export async function createConversation(title?: string) {
  return await fetcher(`${API_BASE}/conversations`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title: title ?? null }),
  });
}

export async function deleteConversation(id: string) {
  await fetcher(`${API_BASE}/conversations/${id}`, {
    method: "DELETE",
  });
}

export async function updateConversationTitle(id: string, title: string) {
  await fetcher(`${API_BASE}/conversations/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
}
