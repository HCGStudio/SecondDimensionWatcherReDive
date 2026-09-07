import fetcher from "../auth/httpClient";
import { ChatAction, ChatActionDecision } from "./types";

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

export async function getChatAction(
  conversationId: string,
  actionId: string,
): Promise<ChatAction> {
  return fetcher(
    `${API_BASE}/conversations/${conversationId}/actions/${actionId}`,
  );
}

export async function approveChatAction(
  action: ChatAction,
  confirmDestructive: boolean,
): Promise<ChatActionDecision> {
  if (!action.approvalToken) throw new Error("Approval token is unavailable");
  return fetcher(
    `${API_BASE}/conversations/${action.conversationId}/actions/${action.id}/approve`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        approvalToken: action.approvalToken,
        parameterHash: action.parameterHash,
        confirmDestructive,
      }),
    },
  );
}

export async function rejectChatAction(action: ChatAction): Promise<void> {
  if (!action.approvalToken) throw new Error("Approval token is unavailable");
  await fetcher(
    `${API_BASE}/conversations/${action.conversationId}/actions/${action.id}/reject`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        approvalToken: action.approvalToken,
        parameterHash: action.parameterHash,
      }),
    },
  );
}
