import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { AiModel, ChatConversation, ChatConversationDetail, ChatStatus } from "./types";

export const useChatStatus = () =>
  useSWR<ChatStatus>("/api/chat/status", fetcher);

export const useChatModels = () =>
  useSWR<AiModel[]>("/api/chat/models", fetcher);

export const useConversations = () =>
  useSWR<ChatConversation[]>("/api/chat/conversations", fetcher, {
    refreshInterval: 0,
  });

export const useConversationMessages = (conversationId: string | null) =>
  useSWR<ChatConversationDetail>(
    conversationId ? `/api/chat/conversations/${conversationId}` : null,
    fetcher,
  );
