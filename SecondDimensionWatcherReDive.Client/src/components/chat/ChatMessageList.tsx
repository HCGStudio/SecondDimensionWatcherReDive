import React, { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";

import { ChatMessageData } from "../../chat/types";
import { StreamingContentBlock } from "../../chat/useStreamingChat";
import { AssistantGroup, UserBubble, StreamingMessage } from "./ChatMessage";

interface ChatMessageListProps {
  messages: ChatMessageData[];
  contentBlocks: StreamingContentBlock[];
  isStreaming: boolean;
  pendingUserMessage?: string | null;
}

/** Group consecutive non-user messages into runs. Each user message is its own group. */
function groupMessages(
  messages: ChatMessageData[],
): { type: "user"; message: ChatMessageData }[] | { type: "assistant"; messages: ChatMessageData[] }[] {
  const groups: (
    | { type: "user"; message: ChatMessageData }
    | { type: "assistant"; messages: ChatMessageData[] }
  )[] = [];

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
}) => {
  const { t } = useTranslation("chat");
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, contentBlocks]);

  const groups = groupMessages(messages);

  return (
    <div className="flex-1 overflow-y-auto px-4 py-6 space-y-4">
      {groups.length === 0 && !isStreaming && !pendingUserMessage && (
        <div className="flex items-center justify-center h-full text-subtle">
          <div className="text-center">
            <p className="font-serif text-lg text-muted">{t("emptyTitle")}</p>
            <p className="text-sm mt-1">
              {t("emptyHelp")}
            </p>
          </div>
        </div>
      )}
      {groups.map((group, i) =>
        group.type === "user" ? (
          <UserBubble key={group.message.id} message={group.message} />
        ) : (
          <AssistantGroup key={group.messages[0].id} messages={group.messages} />
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
      <div ref={bottomRef} />
    </div>
  );
};
