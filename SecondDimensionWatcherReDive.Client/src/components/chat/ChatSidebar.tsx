import React from "react";
import { useTranslation } from "react-i18next";

import { MessageSquare, Plus, Trash2 } from "lucide-react";

import { ChatConversation } from "../../chat/types";
import { cn } from "../../lib/cn";
import { Button } from "../ui/Button";

interface ChatSidebarProps {
  conversations: ChatConversation[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onCreate: () => void | Promise<void>;
  onDelete: (id: string) => void | Promise<void>;
  className?: string;
}

export const ChatSidebar: React.FC<ChatSidebarProps> = ({
  conversations,
  selectedId,
  onSelect,
  onCreate,
  onDelete,
  className,
}) => {
  const { t } = useTranslation("chat");
  const newConversationRef = React.useRef<HTMLButtonElement>(null);
  return (
    <aside
      aria-label={t("conversations")}
      className={cn(
        "flex h-full w-[280px] shrink-0 flex-col border-r border-border bg-surface",
        className,
      )}
    >
      <div className="flex items-center justify-between border-b border-border-light p-4 pr-12">
        <h2 className="font-serif text-base font-medium text-foreground">
          {t("conversations")}
        </h2>
        <Button
          ref={newConversationRef}
          variant="icon"
          size="sm"
          onClick={onCreate}
          title={t("newConversation")}
          aria-label={t("newConversation")}
        >
          <Plus size={18} />
        </Button>
      </div>
      <nav className="flex-1 space-y-0.5 overflow-y-auto p-2">
        {conversations.length === 0 && (
          <div className="px-3 py-8 text-center text-sm text-subtle">
            {t("noConversations")}
          </div>
        )}
        {conversations.map((conv) => (
          <div
            key={conv.id}
            className={cn(
              "group flex items-center rounded-md transition-colors",
              selectedId === conv.id
                ? "bg-canvas text-foreground shadow-ring"
                : "text-muted hover:text-foreground hover:bg-canvas",
            )}
          >
            <button
              type="button"
              aria-current={selectedId === conv.id ? "page" : undefined}
              onClick={() => onSelect(conv.id)}
              className="flex min-w-0 flex-1 items-center gap-2 rounded-md px-3 py-2 text-left focus:outline-hidden focus:ring-2 focus:ring-inset focus:ring-focus"
            >
              <MessageSquare
                size={14}
                className="shrink-0"
                aria-hidden="true"
              />
              <span className="flex-1 truncate text-sm">
                {conv.title || t("untitledConversation")}
              </span>
            </button>
            <button
              type="button"
              onClick={async (e) => {
                e.stopPropagation();
                await onDelete(conv.id);
                newConversationRef.current?.focus();
              }}
              className="mr-2 shrink-0 rounded p-1 text-subtle opacity-100 transition-all hover:text-error focus:outline-hidden focus:ring-2 focus:ring-focus md:opacity-0 md:group-hover:opacity-100 md:focus-visible:opacity-100"
              title={t("deleteConversation")}
              aria-label={t("deleteConversationNamed", {
                name: conv.title || t("untitledConversation"),
              })}
            >
              <Trash2 size={14} />
            </button>
          </div>
        ))}
      </nav>
    </aside>
  );
};
