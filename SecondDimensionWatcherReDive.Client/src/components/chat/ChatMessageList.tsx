import React, { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";

import { ChatMessageData } from "../../chat/types";
import { StreamingContentBlock } from "../../chat/useStreamingChat";
import { AssistantGroup, StreamingMessage, UserBubble } from "./ChatMessage";

interface ChatMessageListProps {
  messages: ChatMessageData[];
  contentBlocks: StreamingContentBlock[];
  isStreaming: boolean;
  pendingUserMessage?: string | null;
  hasStreamError?: boolean;
}

type MessageGroup =
  | { type: "user"; message: ChatMessageData }
  | { type: "assistant"; messages: ChatMessageData[] };

/** Group consecutive non-user messages into runs. Each user message is its own group. */
function groupMessages(messages: ChatMessageData[]): MessageGroup[] {
  const groups: MessageGroup[] = [];

  for (const msg of messages) {
    if (msg.role === "system") continue;

    if (msg.role === "user") {
      groups.push({ type: "user", message: msg });
    } else {
      // assistant or tool — append to current assistant group or start a new one
      const last = groups[groups.length - 1];
      if (last && last.type === "assistant") {
        last.messages.push(msg);
      } else {
        groups.push({ type: "assistant", messages: [msg] });
      }
    }
  }

  return groups;
}

export const ChatMessageList: React.FC<ChatMessageListProps> = ({
  messages,
  contentBlocks,
  isStreaming,
  pendingUserMessage,
  hasStreamError = false,
}) => {
  const { t } = useTranslation("chat");
  const bottomRef = useRef<HTMLDivElement>(null);
  const wasStreaming = useRef(false);
  const [announcement, setAnnouncement] = React.useState("");

  useEffect(() => {
    const reduceMotion = window.matchMedia(
      "(prefers-reduced-motion: reduce)",
    ).matches;
    bottomRef.current?.scrollIntoView({
      behavior: reduceMotion || isStreaming ? "auto" : "smooth",
    });
  }, [messages, contentBlocks, isStreaming]);

  useEffect(() => {
    if (isStreaming && !wasStreaming.current) {
      setAnnouncement(t("streaming"));
    } else if (!isStreaming && wasStreaming.current) {
      setAnnouncement(
        !hasStreamError && contentBlocks.length > 0
          ? t("responseComplete")
          : "",
      );
    }
    wasStreaming.current = isStreaming;
  }, [contentBlocks.length, hasStreamError, isStreaming, t]);

  const groups = groupMessages(messages);

  return (
    <div
      className="flex-1 space-y-4 overflow-y-auto px-4 py-6"
      role="region"
      aria-label={t("messageHistory")}
    >
      <span
        className="sr-only"
        role="status"
        aria-live="polite"
        aria-atomic="true"
      >
        {announcement}
      </span>
      {groups.length === 0 && !isStreaming && !pendingUserMessage && (
        <div className="flex items-center justify-center h-full text-subtle">
          <div className="text-center">
            <p className="font-serif text-lg text-muted">{t("emptyTitle")}</p>
            <p className="text-sm mt-1">{t("emptyHelp")}</p>
          </div>
        </div>
      )}
      {groups.map((group) =>
        group.type === "user" ? (
          <UserBubble key={group.message.id} message={group.message} />
        ) : (
          <AssistantGroup
            key={group.messages[0].id}
            messages={group.messages}
          />
        ),
      )}
      {pendingUserMessage &&
        !messages.some(
          (m) => m.role === "user" && m.content === pendingUserMessage,
        ) && (
          <div className="flex w-full justify-end">
            <div className="max-w-[80%] rounded-md px-4 py-2.5 bg-canvas text-foreground">
              <div className="text-sm leading-relaxed whitespace-pre-wrap">
                {pendingUserMessage}
              </div>
            </div>
          </div>
        )}
      {isStreaming && (
        <StreamingMessage
          contentBlocks={contentBlocks}
          isStreaming={isStreaming}
        />
      )}
      <div ref={bottomRef} aria-hidden="true" />
    </div>
  );
};
