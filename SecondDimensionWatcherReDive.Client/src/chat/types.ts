export interface ChatConversation {
  id: string;
  title: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ChatConversationDetail {
  id: string;
  title: string | null;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessageData[];
}

export interface ChatMessageData {
  id: string;
  role: "system" | "user" | "assistant" | "tool";
  content: string | null;
  toolCallsJson: string | null;
  toolCallId: string | null;
  toolName: string | null;
  order: number;
  createdAt: string;
}

export interface ToolCallInfo {
  id: string;
  name: string;
  arguments: string;
}

export interface AiModel {
  id: string;
  name: string;
  provider: string;
}

export interface ChatStatus {
  aiEnabled: boolean;
  provider: string | null;
}

export function parseToolCalls(json: string | null): ToolCallInfo[] {
  if (!json) return [];
  try {
    return JSON.parse(json);
  } catch {
    return [];
  }
}
