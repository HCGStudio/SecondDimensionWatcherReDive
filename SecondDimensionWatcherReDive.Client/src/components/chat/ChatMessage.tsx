import React from "react";

import { ChatMessageData, parseToolCalls } from "../../chat/types";
import { useTypewriter } from "../../chat/useTypewriter";
import { StreamingContentBlock } from "../../chat/useStreamingChat";
import { MarkdownContent } from "./MarkdownContent";
import { ToolCallDisplay, ToolCallItem } from "./ToolCallDisplay";

/** A single user message bubble (right-aligned). */
export const UserBubble: React.FC<{ message: ChatMessageData }> = ({
  message,
}) => (
  <div className="flex w-full justify-end">
    <div className="max-w-[80%] rounded-md px-4 py-2.5 bg-canvas text-foreground">
      <div className="text-sm leading-relaxed whitespace-pre-wrap">
        {message.content}
      </div>
    </div>
  </div>
);

/**
 * A group of consecutive non-user messages rendered in a single bubble.
 * The group contains assistant messages interleaved with tool result messages,
 * displayed in order: text -> tool calls (with results) -> text -> ...
 */
export const AssistantGroup: React.FC<{
  messages: ChatMessageData[];
}> = ({ messages }) => {
  // Build a map of tool results keyed by toolCallId
  const toolResults = new Map<string, ChatMessageData>();
  for (const msg of messages) {
    if (msg.role === "tool" && msg.toolCallId) {
      toolResults.set(msg.toolCallId, msg);
    }
  }

  // Only render assistant messages; tool messages are inlined via toolResults map
  const assistantMessages = messages.filter((m) => m.role === "assistant");

  return (
    <div className="flex w-full justify-start">
      <div className="max-w-[80%] rounded-md px-4 py-2.5 bg-surface border border-border-light text-foreground">
        {assistantMessages.map((msg, i) => {
          const toolCalls = parseToolCalls(msg.toolCallsJson);
          const toolCallsWithResults = toolCalls.map((tc) => ({
            ...tc,
            result: toolResults.get(tc.id)?.content ?? undefined,
          }));

          return (
            <React.Fragment key={msg.id}>
              {msg.content && <MarkdownContent content={msg.content} />}
              {toolCallsWithResults.length > 0 && (
                <ToolCallDisplay toolCalls={toolCallsWithResults} />
              )}
            </React.Fragment>
          );
        })}
      </div>
    </div>
  );
};

interface StreamingMessageProps {
  contentBlocks: StreamingContentBlock[];
  isStreaming: boolean;
}

export const StreamingMessage: React.FC<StreamingMessageProps> = ({
  contentBlocks,
  isStreaming,
}) => {
  // Find the last text block index for typewriter animation
  const lastTextIdx = contentBlocks.reduce(
    (acc, block, i) => (block.type === "text" ? i : acc),
    -1,
  );

  const lastTextContent =
    lastTextIdx >= 0 && contentBlocks[lastTextIdx].type === "text"
      ? contentBlocks[lastTextIdx].text
      : "";
  const displayedText = useTypewriter(lastTextContent, isStreaming);

  return (
    <div className="flex w-full justify-start">
      <div className="max-w-[80%] rounded-md px-4 py-2.5 bg-surface border border-border-light text-foreground">
        {contentBlocks.map((block, i) => {
          if (block.type === "text") {
            const text = i === lastTextIdx ? displayedText : block.text;
            if (!text) return null;
            return (
              <React.Fragment key={`text-${i}`}>
                <MarkdownContent content={text} />
                {i === lastTextIdx && isStreaming && (
                  <span className="inline-block w-1.5 h-4 bg-foreground/50 animate-pulse ml-0.5 align-text-bottom" />
                )}
              </React.Fragment>
            );
          }
          if (block.type === "tool_call") {
            return (
              <div key={block.toolCall.id} className="my-2">
                <ToolCallItem
                  toolCall={block.toolCall}
                  isStreaming={isStreaming}
                />
              </div>
            );
          }
          return null;
        })}
        {isStreaming && contentBlocks.length === 0 && (
          <div className="text-sm text-subtle animate-pulse">思考中...</div>
        )}
      </div>
    </div>
  );
};
