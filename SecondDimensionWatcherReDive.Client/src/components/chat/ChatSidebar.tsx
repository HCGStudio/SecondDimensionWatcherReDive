import { MessageSquare, Plus, Trash2 } from "lucide-react";
import React from "react";

import { ChatConversation } from "../../chat/types";
import { cn } from "../../lib/cn";
import { Button } from "../ui/Button";

interface ChatSidebarProps {
  conversations: ChatConversation[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onCreate: () => void;
  onDelete: (id: string) => void;
}

export const ChatSidebar: React.FC<ChatSidebarProps> = ({
  conversations,
  selectedId,
  onSelect,
  onCreate,
  onDelete,
}) => {
  return (
    <div className="flex h-full w-[280px] flex-col border-r border-border bg-surface">
      <div className="flex items-center justify-between p-4 border-b border-border-light">
        <h2 className="font-serif text-base font-medium text-foreground">
          对话
        </h2>
        <Button
          variant="icon"
          size="sm"
          onClick={onCreate}
          title="新建对话"
        >
          <Plus size={18} />
        </Button>
      </div>
      <div className="flex-1 overflow-y-auto p-2 space-y-0.5">
        {conversations.length === 0 && (
          <div className="px-3 py-8 text-center text-sm text-subtle">
            暂无对话
          </div>
        )}
        {conversations.map((conv) => (
          <div
            key={conv.id}
            className={cn(
              "group flex items-center gap-2 rounded-md px-3 py-2 cursor-pointer transition-colors",
              selectedId === conv.id
                ? "bg-canvas text-foreground shadow-ring"
                : "text-muted hover:text-foreground hover:bg-canvas",
            )}
            onClick={() => onSelect(conv.id)}
          >
            <MessageSquare size={14} className="flex-shrink-0" />
            <span className="flex-1 truncate text-sm">
              {conv.title || "新对话"}
            </span>
            <button
              onClick={(e) => {
                e.stopPropagation();
                onDelete(conv.id);
              }}
              className="hidden group-hover:block flex-shrink-0 rounded p-0.5 text-subtle hover:text-error transition-colors"
              title="删除对话"
            >
              <Trash2 size={14} />
            </button>
          </div>
        ))}
      </div>
    </div>
  );
};
