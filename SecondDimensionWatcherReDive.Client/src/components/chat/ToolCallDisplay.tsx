import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { ChevronDown, ChevronRight } from "lucide-react";

import { ToolCallInfo } from "../../chat/types";
import { StreamingToolCall } from "../../chat/useStreamingChat";

interface ToolCallDisplayProps {
  toolCalls: (ToolCallInfo & { result?: string })[] | StreamingToolCall[];
  isStreaming?: boolean;
}

export const ToolCallDisplay: React.FC<ToolCallDisplayProps> = ({
  toolCalls,
  isStreaming,
}) => {
  if (toolCalls.length === 0) return null;

  return (
    <div className="mt-2 space-y-1.5">
      {toolCalls.map((tc, i) => (
        <ToolCallItem
          key={tc.id || i}
          toolCall={tc}
          isStreaming={isStreaming}
        />
      ))}
    </div>
  );
};

export const ToolCallItem: React.FC<{
  toolCall: (ToolCallInfo & { result?: string }) | StreamingToolCall;
  isStreaming?: boolean;
}> = ({ toolCall, isStreaming }) => {
  const { t } = useTranslation("chat");
  const [expanded, setExpanded] = useState(false);
  const contentId = React.useId();

  return (
    <div className="rounded-md border border-border-light bg-canvas text-sm">
      <button
        type="button"
        aria-expanded={expanded}
        aria-controls={contentId}
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center gap-1.5 rounded-md px-3 py-1.5 text-left text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-inset focus:ring-focus"
      >
        {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        <span className="font-mono text-xs">{toolCall.name}</span>
        {isStreaming && !("result" in toolCall && toolCall.result) && (
          <span className="ml-auto text-xs text-subtle animate-pulse">
            {t("tool.executing")}
          </span>
        )}
      </button>
      {expanded && (
        <div
          id={contentId}
          className="space-y-2 border-t border-border-light px-3 py-2"
        >
          {toolCall.arguments && (
            <div>
              <div className="text-xs text-subtle mb-1">
                {t("tool.arguments")}
              </div>
              <pre className="text-xs font-mono bg-canvas rounded p-2 overflow-x-auto whitespace-pre-wrap break-all">
                {formatJson(toolCall.arguments)}
              </pre>
            </div>
          )}
          {"result" in toolCall && toolCall.result && (
            <div>
              <div className="text-xs text-subtle mb-1">{t("tool.result")}</div>
              <pre className="text-xs font-mono bg-canvas rounded p-2 overflow-x-auto whitespace-pre-wrap break-all max-h-48 overflow-y-auto">
                {formatJson(toolCall.result)}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

function formatJson(str: string): string {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
}
