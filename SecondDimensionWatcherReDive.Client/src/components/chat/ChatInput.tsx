import { Send } from "lucide-react";
import React, { useCallback, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "../ui/Button";

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled?: boolean;
}

export const ChatInput: React.FC<ChatInputProps> = ({ onSend, disabled }) => {
  const { t } = useTranslation("chat");
  const [value, setValue] = useState("");
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleSend = useCallback(() => {
    const trimmed = value.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    setValue("");
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }
  }, [value, disabled, onSend]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleInput = () => {
    const el = textareaRef.current;
    if (el) {
      el.style.height = "auto";
      el.style.height = Math.min(el.scrollHeight, 160) + "px";
    }
  };

  return (
    <div className="border-t border-border bg-surface px-4 py-3">
      <div className="flex items-end gap-2">
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKeyDown}
          onInput={handleInput}
          placeholder={t("inputPlaceholder")}
          disabled={disabled}
          rows={1}
          className="flex-1 resize-none rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-foreground placeholder:text-subtle focus:outline-hidden focus:border-focus transition-colors scrollbar-none"
        />
        <Button
          variant="solid"
          size="sm"
          onClick={handleSend}
          disabled={disabled || !value.trim()}
          className="flex-shrink-0 p-2.5"
        >
          <Send size={16} />
        </Button>
      </div>
    </div>
  );
};
