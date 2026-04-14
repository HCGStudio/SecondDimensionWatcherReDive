const API_BASE = "/api/chat";

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

export async function createConversation(title?: string) {
  const res = await fetch(`${API_BASE}/conversations`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...getAuthHeaders() },
    body: JSON.stringify({ title: title ?? null }),
  });
  if (!res.ok) throw new Error("Failed to create conversation");
  return res.json();
}

export async function deleteConversation(id: string) {
  const res = await fetch(`${API_BASE}/conversations/${id}`, {
    method: "DELETE",
    headers: getAuthHeaders(),
  });
  if (!res.ok) throw new Error("Failed to delete conversation");
}

export async function updateConversationTitle(id: string, title: string) {
  const res = await fetch(`${API_BASE}/conversations/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json", ...getAuthHeaders() },
    body: JSON.stringify({ title }),
  });
  if (!res.ok) throw new Error("Failed to update title");
}
