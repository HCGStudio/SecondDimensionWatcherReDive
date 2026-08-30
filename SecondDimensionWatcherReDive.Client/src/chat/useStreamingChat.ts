import { useCallback, useEffect, useReducer, useRef } from "react";

import {
  AuthBoundRequest,
  AuthIdentityChangedError,
  beginAuthBoundRequest,
} from "../auth/httpClient";

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
  error: string | null;
}

type StreamingAction =
  | { type: "start" }
  | { type: "text_delta"; text: string }
  | { type: "tool_call_begin"; id: string; name: string }
  | { type: "tool_call_delta"; id: string; argumentsDelta: string }
  | { type: "tool_result"; toolCallId: string; name: string; result: string }
  | { type: "finished" }
  | { type: "error"; message: string }
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
      return { ...state, isStreaming: false, error: action.message };

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
  const activeRequestRef = useRef<AuthBoundRequest | null>(null);
  const requestGenerationRef = useRef(0);

  const sendMessage = useCallback(
    async (conversationId: string, content: string, model?: string) => {
      activeRequestRef.current?.abort();
      activeRequestRef.current?.dispose();
      const generation = requestGenerationRef.current + 1;
      requestGenerationRef.current = generation;
      dispatch({ type: "start" });

      let request: AuthBoundRequest | null = null;
      try {
        request = beginAuthBoundRequest(true);
        activeRequestRef.current = request;
        const response = await fetch(
          `/api/chat/conversations/${conversationId}/messages`,
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              Authorization: `Bearer ${request.auth.token}`,
            },
            body: JSON.stringify({ content, model: model ?? null }),
            signal: request.signal,
          },
        );
        if (!request.isCurrent()) throw new AuthIdentityChangedError();

        if (!response.ok) {
          const text = await response.text();
          if (requestGenerationRef.current === generation) {
            dispatch({
              type: "error",
              message: text || `HTTP ${response.status}`,
            });
          }
          return;
        }

        const reader = response.body?.getReader();
        if (!reader) {
          dispatch({ type: "error", message: "No response body" });
          return;
        }

        const decoder = new TextDecoder();
        let buffer = "";
        let receivedFinished = false;

        while (true) {
          const { done, value } = await reader.read();
          if (!request.isCurrent()) throw new AuthIdentityChangedError();
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
                    if (requestGenerationRef.current === generation) {
                      dispatch({ type: "text_delta", text: data.text });
                    }
                    break;
                  case "tool_call_begin":
                    if (requestGenerationRef.current === generation) {
                      dispatch({
                        type: "tool_call_begin",
                        id: data.id,
                        name: data.name,
                      });
                    }
                    break;
                  case "tool_call_delta":
                    if (requestGenerationRef.current === generation) {
                      dispatch({
                        type: "tool_call_delta",
                        id: data.id,
                        argumentsDelta: data.arguments_delta,
                      });
                    }
                    break;
                  case "tool_result":
                    if (requestGenerationRef.current === generation) {
                      dispatch({
                        type: "tool_result",
                        toolCallId: data.tool_call_id,
                        name: data.name,
                        result: data.result,
                      });
                    }
                    break;
                  case "finished":
                    receivedFinished = true;
                    if (requestGenerationRef.current === generation) {
                      dispatch({ type: "finished" });
                    }
                    break;
                  case "error":
                    if (requestGenerationRef.current === generation) {
                      dispatch({ type: "error", message: data.message });
                    }
                    break;
                }
              } catch {
                // Skip malformed JSON
              }
              currentEvent = "";
            }
          }
        }

        if (!receivedFinished && requestGenerationRef.current === generation) {
          dispatch({ type: "finished" });
        }
      } catch (err) {
        if (requestGenerationRef.current !== generation) return;
        if (
          err instanceof AuthIdentityChangedError ||
          (err instanceof DOMException && err.name === "AbortError")
        ) {
          dispatch({ type: "reset" });
        } else {
          dispatch({
            type: "error",
            message: err instanceof Error ? err.message : "Unknown error",
          });
        }
      } finally {
        request?.dispose();
        if (activeRequestRef.current === request) {
          activeRequestRef.current = null;
        }
      }
    },
    [],
  );

  const reset = useCallback(() => {
    requestGenerationRef.current += 1;
    activeRequestRef.current?.abort();
    activeRequestRef.current?.dispose();
    activeRequestRef.current = null;
    dispatch({ type: "reset" });
  }, []);

  useEffect(
    () => () => {
      requestGenerationRef.current += 1;
      activeRequestRef.current?.abort();
      activeRequestRef.current?.dispose();
      activeRequestRef.current = null;
    },
    [],
  );

  return { ...state, sendMessage, reset };
}

export type { StreamingToolCall, StreamingContentBlock };
