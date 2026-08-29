import React, { useCallback, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Send } from "lucide-react";

import { Button } from "../ui/Button";

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled?: boolean;
}

export const ChatInput: React.FC<ChatInputProps> = ({ onSend, disabled }) => {
  const { t } = useTranslation("chat");
  const [value, setValue] = useState("");
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const inputId = React.useId();

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
    <form
      className="border-t border-border bg-surface px-4 py-3"
      onSubmit={(event) => {
        event.preventDefault();
        handleSend();
      }}
    >
      <div className="flex items-end gap-2">
        <label htmlFor={inputId} className="sr-only">
          {t("inputLabel")}
        </label>
        <textarea
          id={inputId}
          ref={textareaRef}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKeyDown}
          onInput={handleInput}
          placeholder={t("inputPlaceholder")}
          disabled={disabled}
          rows={1}
          className="scrollbar-none flex-1 resize-none rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-foreground placeholder:text-subtle transition-colors focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus"
        />
        <Button
          type="submit"
          variant="solid"
          size="sm"
          disabled={disabled || !value.trim()}
          className="flex-shrink-0 p-2.5"
          aria-label={t("send")}
        >
          <Send size={16} />
        </Button>
      </div>
    </form>
  );
};
