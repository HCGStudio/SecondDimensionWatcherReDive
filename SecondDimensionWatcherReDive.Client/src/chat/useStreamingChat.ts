import { useCallback, useEffect, useReducer, useRef } from "react";

interface StreamingToolCall {
  id: string;
  name: string;
  arguments: string;
  result?: string;
}

type StreamingContentBlock =
  | { type: "text"; text: string }
  | { type: "tool_call"; toolCall: StreamingToolCall };

interface StreamingState {
  isStreaming: boolean;
  contentBlocks: StreamingContentBlock[];
  error: ChatStreamErrorCode | null;
}

type ChatStreamErrorCode =
  | "notAuthenticated"
  | "invalidAuthentication"
  | "unauthorized"
  | "rateLimited"
  | "serviceUnavailable"
  | "requestFailed"
  | "emptyResponse"
  | "streamFailed"
  | "connectionFailed";

type StreamingAction =
  | { type: "start" }
  | { type: "text_delta"; text: string }
  | { type: "tool_call_begin"; id: string; name: string }
  | { type: "tool_call_delta"; id: string; argumentsDelta: string }
  | { type: "tool_result"; toolCallId: string; name: string; result: string }
  | { type: "finished" }
  | { type: "error"; code: ChatStreamErrorCode }
  | { type: "reset" };

function reducer(
  state: StreamingState,
  action: StreamingAction,
): StreamingState {
  switch (action.type) {
    case "start":
      return { isStreaming: true, contentBlocks: [], error: null };

    case "text_delta": {
      const blocks = [...state.contentBlocks];
      const last = blocks[blocks.length - 1];
      if (last && last.type === "text") {
        blocks[blocks.length - 1] = {
          type: "text",
          text: last.text + action.text,
        };
      } else {
        blocks.push({ type: "text", text: action.text });
      }
      return { ...state, contentBlocks: blocks };
    }

    case "tool_call_begin":
      return {
        ...state,
        contentBlocks: [
          ...state.contentBlocks,
          {
            type: "tool_call",
            toolCall: { id: action.id, name: action.name, arguments: "" },
          },
        ],
      };

    case "tool_call_delta":
      return {
        ...state,
        contentBlocks: state.contentBlocks.map((block) =>
          block.type === "tool_call" && block.toolCall.id === action.id
            ? {
                ...block,
                toolCall: {
                  ...block.toolCall,
                  arguments: block.toolCall.arguments + action.argumentsDelta,
                },
              }
            : block,
        ),
      };

    case "tool_result":
      return {
        ...state,
        contentBlocks: state.contentBlocks.map((block) =>
          block.type === "tool_call" && block.toolCall.id === action.toolCallId
            ? {
                ...block,
                toolCall: { ...block.toolCall, result: action.result },
              }
            : block,
        ),
      };

    case "finished":
      return { ...state, isStreaming: false };

    case "error":
      return { ...state, isStreaming: false, error: action.code };

    case "reset":
      return { isStreaming: false, contentBlocks: [], error: null };

    default:
      return state;
  }
}

const initialState: StreamingState = {
  isStreaming: false,
  contentBlocks: [],
  error: null,
};

export function useStreamingChat() {
  const [state, dispatch] = useReducer(reducer, initialState);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => () => abortRef.current?.abort(), []);

  const sendMessage = useCallback(
    async (conversationId: string, content: string, model?: string) => {
      dispatch({ type: "start" });

      const authStr = localStorage.getItem("auth");
      if (!authStr) {
        dispatch({ type: "error", code: "notAuthenticated" });
        return;
      }

      let token: string;
      try {
        const auth: unknown = JSON.parse(authStr);
        if (
          !auth ||
          typeof auth !== "object" ||
          typeof (auth as { token?: unknown }).token !== "string"
        ) {
          throw new TypeError();
        }
        token = (auth as { token: string }).token;
      } catch {
        dispatch({ type: "error", code: "invalidAuthentication" });
        return;
      }

      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      try {
        const response = await fetch(
          `/api/chat/conversations/${conversationId}/messages`,
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              Authorization: `Bearer ${token}`,
            },
            body: JSON.stringify({ content, model: model ?? null }),
            signal: controller.signal,
          },
        );

        if (!response.ok) {
          dispatch({
            type: "error",
            code:
              response.status === 401
                ? "unauthorized"
                : response.status === 429
                  ? "rateLimited"
                  : response.status >= 500
                    ? "serviceUnavailable"
                    : "requestFailed",
          });
          return;
        }

        const reader = response.body?.getReader();
        if (!reader) {
          dispatch({ type: "error", code: "emptyResponse" });
          return;
        }

        const decoder = new TextDecoder();
        let buffer = "";
        let receivedFinished = false;

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split("\n");
          buffer = lines.pop() ?? "";

          let currentEvent = "";
          for (const line of lines) {
            if (line.startsWith("event: ")) {
              currentEvent = line.slice(7).trim();
            } else if (line.startsWith("data: ") && currentEvent) {
              try {
                const data = JSON.parse(line.slice(6));
                switch (currentEvent) {
                  case "text_delta":
                    dispatch({ type: "text_delta", text: data.text });
                    break;
                  case "tool_call_begin":
                    dispatch({
                      type: "tool_call_begin",
                      id: data.id,
                      name: data.name,
                    });
                    break;
                  case "tool_call_delta":
                    dispatch({
                      type: "tool_call_delta",
                      id: data.id,
                      argumentsDelta: data.arguments_delta,
                    });
                    break;
                  case "tool_result":
                    dispatch({
                      type: "tool_result",
                      toolCallId: data.tool_call_id,
                      name: data.name,
                      result: data.result,
                    });
                    break;
                  case "finished":
                    receivedFinished = true;
                    dispatch({ type: "finished" });
                    break;
                  case "error":
                    dispatch({ type: "error", code: "streamFailed" });
                    break;
                }
              } catch {
                // Skip malformed JSON
              }
              currentEvent = "";
            }
          }
        }

        if (!receivedFinished) {
          dispatch({ type: "finished" });
        }
      } catch {
        if (controller.signal.aborted) return;
        dispatch({
          type: "error",
          code: "connectionFailed",
        });
      } finally {
        if (abortRef.current === controller) abortRef.current = null;
      }
    },
    [],
  );

  const reset = useCallback(() => {
    abortRef.current?.abort();
    abortRef.current = null;
    dispatch({ type: "reset" });
  }, []);

  return { ...state, sendMessage, reset };
}

export type { ChatStreamErrorCode, StreamingToolCall, StreamingContentBlock };
