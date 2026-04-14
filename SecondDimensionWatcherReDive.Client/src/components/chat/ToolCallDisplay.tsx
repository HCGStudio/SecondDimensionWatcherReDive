import { ChevronDown, ChevronRight } from "lucide-react";
import React, { useState } from "react";

import { StreamingToolCall } from "../../chat/useStreamingChat";
import { ToolCallInfo } from "../../chat/types";

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
        <ToolCallItem key={tc.id || i} toolCall={tc} isStreaming={isStreaming} />
      ))}
    </div>
  );
};

export const ToolCallItem: React.FC<{
  toolCall: (ToolCallInfo & { result?: string }) | StreamingToolCall;
  isStreaming?: boolean;
}> = ({ toolCall, isStreaming }) => {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="rounded-md border border-border-light bg-canvas text-sm">
      <button
        type="button"
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center gap-1.5 px-3 py-1.5 text-left text-muted hover:text-foreground transition-colors"
      >
        {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        <span className="font-mono text-xs">{toolCall.name}</span>
        {isStreaming && !("result" in toolCall && toolCall.result) && (
          <span className="ml-auto text-xs text-subtle animate-pulse">
            执行中...
          </span>
        )}
      </button>
      {expanded && (
        <div className="border-t border-border-light px-3 py-2 space-y-2">
          {toolCall.arguments && (
            <div>
              <div className="text-xs text-subtle mb-1">参数</div>
              <pre className="text-xs font-mono bg-canvas rounded p-2 overflow-x-auto whitespace-pre-wrap break-all">
                {formatJson(toolCall.arguments)}
              </pre>
            </div>
          )}
          {"result" in toolCall && toolCall.result && (
            <div>
              <div className="text-xs text-subtle mb-1">结果</div>
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
