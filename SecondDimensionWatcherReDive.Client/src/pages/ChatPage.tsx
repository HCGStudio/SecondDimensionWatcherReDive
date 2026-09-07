import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router";

import { MessageSquare, PanelLeft } from "lucide-react";

import { createConversation, deleteConversation } from "../chat/api";
import {
  useChatModels,
  useChatStatus,
  useConversationMessages,
  useConversations,
} from "../chat/hooks";
import { useStreamingChat } from "../chat/useStreamingChat";
import { AppHeader } from "../components/AppHeader";
import { useToast } from "../components/ToastProvider";
import { ChatInput } from "../components/chat/ChatInput";
import { ChatMessageList } from "../components/chat/ChatMessageList";
import { ChatSidebar } from "../components/chat/ChatSidebar";
import { ModelPicker } from "../components/chat/ModelPicker";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Sheet, SheetContent, SheetTitle } from "../components/ui/Sheet";
import { Spinner } from "../components/ui/Spinner";

export const ChatPage: React.FC = () => {
  const { t } = useTranslation("chat");
  const { conversationId } = useParams<{ conversationId?: string }>();
  const navigate = useNavigate();
  const { addToast } = useToast();
  const { data: chatStatus, isLoading: statusLoading } = useChatStatus();
  const { data: models } = useChatModels();
  const { data: conversations, mutate: mutateConversations } =
    useConversations();
  const [selectedConvId, setSelectedConvId] = useState<string | null>(
    conversationId ?? null,
  );
  const [selectedModel, setSelectedModel] = useState<string | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const sidebarTriggerRef = React.useRef<HTMLButtonElement | null>(null);
  const { data: conversationDetail, mutate: mutateMessages } =
    useConversationMessages(selectedConvId);

  const [pendingUserMessage, setPendingUserMessage] = useState<string | null>(
    null,
  );

  const {
    isStreaming,
    contentBlocks,
    error: streamError,
    sendMessage,
    reset: resetStreaming,
  } = useStreamingChat();

  // Sync URL param with selected conversation
  useEffect(() => {
    const fromUrl = conversationId ?? null;
    if (fromUrl !== selectedConvId) {
      setSelectedConvId(fromUrl);
      resetStreaming();
    }
  }, [conversationId]);

  // Auto-select the first model and recover when a settings change removes
  // the previously selected model.
  useEffect(() => {
    if (!models) return;
    if (models.length === 0) setSelectedModel(null);
    else if (
      !selectedModel ||
      !models.some((model) => model.id === selectedModel)
    )
      setSelectedModel(models[0].id);
  }, [models, selectedModel]);

  const handleCreateConversation = useCallback(async () => {
    try {
      const conv = await createConversation();
      await mutateConversations();
      setSidebarOpen(false);
      navigate(`/chat/${conv.id}`);
    } catch {
      addToast({ title: t("toast.createFailed"), color: "danger" });
    }
  }, [addToast, mutateConversations, navigate, t]);

  const handleDeleteConversation = useCallback(
    async (id: string) => {
      try {
        await deleteConversation(id);
        await mutateConversations();
        if (selectedConvId === id) {
          navigate("/chat");
        }
      } catch {
        addToast({ title: t("toast.deleteFailed"), color: "danger" });
      }
    },
    [addToast, selectedConvId, mutateConversations, navigate, t],
  );

  const handleSelectConversation = useCallback(
    (id: string) => {
      setSidebarOpen(false);
      navigate(`/chat/${id}`);
    },
    [navigate],
  );

  const handleSendMessage = useCallback(
    async (content: string) => {
      if (!selectedConvId) return;

      setPendingUserMessage(content);
      await sendMessage(selectedConvId, content, selectedModel ?? undefined);

      // After streaming finishes, refresh messages and conversations
      await Promise.all([mutateMessages(), mutateConversations()]);
      setPendingUserMessage(null);
    },
    [
      selectedConvId,
      selectedModel,
      sendMessage,
      mutateMessages,
      mutateConversations,
    ],
  );

  // Loading state
  if (statusLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-canvas">
        <Spinner />
      </div>
    );
  }

  // AI not configured
  if (chatStatus && !chatStatus.aiEnabled) {
    return (
      <div className="min-h-screen bg-canvas">
        <AppHeader />
        <main className="mx-auto max-w-5xl px-6 py-8">
          <EmptyPrompt
            icon={<MessageSquare size={48} className="text-subtle" />}
            title={t("aiNotConfigured")}
            body={t("aiNotConfiguredHelp")}
            actions={
              <Button
                variant="outline"
                onClick={() => navigate("/settings?section=ai")}
              >
                {t("openAiSettings")}
              </Button>
            }
          />
        </main>
      </div>
    );
  }

  return (
    <div className="flex h-screen flex-col bg-canvas">
      <AppHeader />
      <div className="flex min-h-0 flex-1 overflow-hidden">
        <ChatSidebar
          conversations={conversations ?? []}
          selectedId={selectedConvId}
          onSelect={handleSelectConversation}
          onCreate={handleCreateConversation}
          onDelete={handleDeleteConversation}
          className="hidden md:flex"
        />
        <Sheet open={sidebarOpen} onOpenChange={setSidebarOpen}>
          <SheetContent
            side="left"
            className="w-[min(20rem,calc(100vw-2rem))] max-w-none p-0 md:hidden"
            onCloseAutoFocus={(event) => {
              event.preventDefault();
              sidebarTriggerRef.current?.focus();
            }}
          >
            <SheetTitle className="sr-only">{t("conversations")}</SheetTitle>
            <ChatSidebar
              conversations={conversations ?? []}
              selectedId={selectedConvId}
              onSelect={handleSelectConversation}
              onCreate={handleCreateConversation}
              onDelete={handleDeleteConversation}
              className="w-full border-r-0"
            />
          </SheetContent>
        </Sheet>
        <div className="flex min-w-0 flex-1 flex-col">
          {/* Chat header with model picker */}
          <div className="flex min-w-0 items-center justify-between gap-2 border-b border-border-light px-3 py-2 sm:px-4">
            <div className="flex min-w-0 items-center gap-2 text-sm text-muted">
              <Button
                variant="icon"
                size="sm"
                className="shrink-0 md:hidden"
                ref={sidebarTriggerRef}
                aria-label={t("openConversations")}
                aria-expanded={sidebarOpen}
                onClick={() => setSidebarOpen(true)}
              >
                <PanelLeft size={18} />
              </Button>
              {conversationDetail?.title && (
                <span className="truncate font-medium text-foreground">
                  {conversationDetail.title}
                </span>
              )}
            </div>
            {models && models.length > 0 && (
              <ModelPicker
                models={models}
                selectedModel={selectedModel}
                onSelect={setSelectedModel}
              />
            )}
          </div>

          {/* Messages area */}
          {selectedConvId ? (
            <>
              <ChatMessageList
                messages={conversationDetail?.messages ?? []}
                contentBlocks={contentBlocks}
                isStreaming={isStreaming}
                pendingUserMessage={pendingUserMessage}
                hasStreamError={!!streamError}
              />
              {streamError && (
                <div
                  role="alert"
                  className="mx-4 mb-2 rounded-md border border-error/20 bg-surface px-3 py-2 text-sm text-error"
                >
                  {t(`errors.${streamError}`)}
                </div>
              )}
              <ChatInput onSend={handleSendMessage} disabled={isStreaming} />
            </>
          ) : (
            <div className="flex flex-1 items-center justify-center">
              <div className="text-center">
                <MessageSquare size={48} className="mx-auto mb-4 text-subtle" />
                <h1 className="font-serif text-lg text-muted">
                  {t("selectOrCreate")}
                </h1>
                <p className="text-sm text-subtle mt-1">{t("createHint")}</p>
                <Button
                  variant="solid"
                  size="sm"
                  onClick={handleCreateConversation}
                  className="mt-4"
                >
                  {t("newConversation")}
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
