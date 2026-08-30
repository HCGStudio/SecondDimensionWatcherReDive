import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  AlertTriangle,
  Check,
  ChevronDown,
  ChevronRight,
  X,
} from "lucide-react";

import {
  approveChatAction,
  getChatAction,
  rejectChatAction,
} from "../../chat/api";
import { ChatAction, ToolCallInfo } from "../../chat/types";
import { StreamingToolCall } from "../../chat/useStreamingChat";
import { Button } from "../ui/Button";

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
  const approval =
    "approval" in toolCall && toolCall.approval
      ? { action: toolCall.approval }
      : parseApprovalReference(
          "result" in toolCall ? toolCall.result : undefined,
        );

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
            {t("tool.executing")}
          </span>
        )}
      </button>
      {expanded && (
        <div className="border-t border-border-light px-3 py-2 space-y-2">
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
      {approval && (
        <ApprovalCard
          initialAction={"action" in approval ? approval.action : undefined}
          actionId={"actionId" in approval ? approval.actionId : undefined}
          conversationId={
            "conversationId" in approval ? approval.conversationId : undefined
          }
        />
      )}
    </div>
  );
};

const ApprovalCard: React.FC<{
  initialAction?: ChatAction;
  actionId?: string;
  conversationId?: string;
}> = ({ initialAction, actionId, conversationId }) => {
  const { t } = useTranslation("chat");
  const [action, setAction] = useState<ChatAction | undefined>(initialAction);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const resolvedActionId = initialAction?.id ?? actionId;
  const resolvedConversationId =
    initialAction?.conversationId ?? conversationId;
  useEffect(() => {
    if (!resolvedActionId || !resolvedConversationId) return;
    let active = true;
    getChatAction(resolvedConversationId, resolvedActionId)
      .then((current) => {
        if (active) setAction(current);
      })
      .catch(() => {
        if (active) setError(t("approval.loadFailed"));
      });
    return () => {
      active = false;
    };
  }, [resolvedActionId, resolvedConversationId, t]);

  const expired = useMemo(
    () => !!action && new Date(action.expiresAt).getTime() <= Date.now(),
    [action],
  );
  if (!action) {
    return (
      <div className="border-t border-border-light px-3 py-2 text-xs text-subtle">
        {error ?? t("approval.loading")}
      </div>
    );
  }

  const isPending = action.state === "Pending" && !expired;
  const handleApprove = async () => {
    if (action.riskLevel === "Destructive" && !confirming) {
      setConfirming(true);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const decision = await approveChatAction(
        action,
        action.riskLevel === "Destructive",
      );
      if (decision.action) setAction(decision.action);
    } catch {
      setError(t("approval.decisionFailed"));
      const current = await getChatAction(
        action.conversationId,
        action.id,
      ).catch(() => null);
      if (current) setAction(current);
    } finally {
      setBusy(false);
    }
  };

  const handleReject = async () => {
    setBusy(true);
    setError(null);
    try {
      await rejectChatAction(action);
      const current = await getChatAction(action.conversationId, action.id);
      setAction(current);
    } catch {
      setError(t("approval.decisionFailed"));
    } finally {
      setBusy(false);
    }
  };

  const effectiveState =
    expired && action.state === "Pending" ? "Expired" : action.state;
  return (
    <div className="border-t border-border-light bg-surface px-3 py-3 space-y-2.5">
      <div className="flex items-start gap-2">
        <AlertTriangle
          size={16}
          className={
            action.riskLevel === "Destructive"
              ? "mt-0.5 shrink-0 text-error"
              : "mt-0.5 shrink-0 text-warning"
          }
        />
        <div className="min-w-0">
          <div className="text-xs font-semibold text-foreground">
            {t("approval.title")}
          </div>
          <p className="mt-1 text-xs leading-relaxed text-muted">
            {action.impactSummary}
          </p>
          <div className="mt-1 text-[11px] text-subtle">
            {t("approval.risk", {
              risk: t(`approval.risks.${action.riskLevel}`),
            })}
            {" · "}
            {action.isReversible
              ? t("approval.reversible")
              : t("approval.notReversible")}
          </div>
        </div>
        <span className="ml-auto shrink-0 rounded-full bg-canvas px-2 py-0.5 text-[11px] text-muted">
          {t(`approval.states.${effectiveState}`)}
        </span>
      </div>

      {confirming && isPending && (
        <div className="rounded-md border border-error/30 bg-error/5 px-2.5 py-2 text-xs text-error">
          {t("approval.destructiveConfirm")}
        </div>
      )}
      {error && <div className="text-xs text-error">{error}</div>}
      {isPending && (
        <div className="flex justify-end gap-2">
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={handleReject}
            disabled={busy}
          >
            <X size={13} />
            {t("approval.reject")}
          </Button>
          <Button
            type="button"
            size="sm"
            color={action.riskLevel === "Destructive" ? "danger" : "warning"}
            onClick={handleApprove}
            disabled={busy}
          >
            <Check size={13} />
            {confirming ? t("approval.confirmExecute") : t("approval.approve")}
          </Button>
        </div>
      )}
      {(action.resultSummary || action.errorSummary) && (
        <div
          className={`text-xs ${action.errorSummary ? "text-error" : "text-muted"}`}
        >
          {action.errorSummary ?? action.resultSummary}
        </div>
      )}
      {action.toolResult && (
        <pre className="max-h-32 overflow-auto whitespace-pre-wrap break-all rounded bg-canvas p-2 text-xs font-mono">
          {formatJson(action.toolResult)}
        </pre>
      )}
    </div>
  );
};

function parseApprovalReference(
  result?: string,
): { actionId: string; conversationId: string } | null {
  if (!result) return null;
  try {
    const parsed = JSON.parse(result);
    const payload = parsed?.result;
    if (
      payload?.approval_required === true &&
      typeof payload.action_id === "string" &&
      typeof payload.conversation_id === "string"
    ) {
      return {
        actionId: payload.action_id,
        conversationId: payload.conversation_id,
      };
    }
  } catch {
    // Not an approval result.
  }
  return null;
}

function formatJson(str: string): string {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
}
