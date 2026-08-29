import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router";

import { MessageSquare } from "lucide-react";

import { createConversation, deleteConversation } from "../chat/api";
import {
  useChatModels,
  useChatStatus,
  useConversationMessages,
  useConversations,
} from "../chat/hooks";
import { useStreamingChat } from "../chat/useStreamingChat";
import { AppHeader } from "../components/AppHeader";
import { ChatInput } from "../components/chat/ChatInput";
import { ChatMessageList } from "../components/chat/ChatMessageList";
import { ChatSidebar } from "../components/chat/ChatSidebar";
import { ModelPicker } from "../components/chat/ModelPicker";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";

export const ChatPage: React.FC = () => {
  const { t } = useTranslation("chat");
  const { conversationId } = useParams<{ conversationId?: string }>();
  const navigate = useNavigate();
  const { data: chatStatus, isLoading: statusLoading } = useChatStatus();
  const { data: models } = useChatModels();
  const { data: conversations, mutate: mutateConversations } =
    useConversations();
  const [selectedConvId, setSelectedConvId] = useState<string | null>(
    conversationId ?? null,
  );
  const [selectedModel, setSelectedModel] = useState<string | null>(null);
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
      navigate(`/chat/${conv.id}`);
    } catch (err) {
      console.error("Failed to create conversation:", err);
    }
  }, [mutateConversations, navigate]);

  const handleDeleteConversation = useCallback(
    async (id: string) => {
      try {
        await deleteConversation(id);
        await mutateConversations();
        if (selectedConvId === id) {
          navigate("/chat");
        }
      } catch (err) {
        console.error("Failed to delete conversation:", err);
      }
    },
    [selectedConvId, mutateConversations, navigate],
  );

  const handleSelectConversation = useCallback(
    (id: string) => {
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
      <div className="flex flex-1 overflow-hidden">
        <ChatSidebar
          conversations={conversations ?? []}
          selectedId={selectedConvId}
          onSelect={handleSelectConversation}
          onCreate={handleCreateConversation}
          onDelete={handleDeleteConversation}
        />
        <div className="flex flex-1 flex-col">
          {/* Chat header with model picker */}
          <div className="flex items-center justify-between border-b border-border-light px-4 py-2">
            <div className="text-sm text-muted">
              {conversationDetail?.title && (
                <span className="font-medium text-foreground">
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
              />
              {streamError && (
                <div className="mx-4 mb-2 rounded-md border border-error/20 bg-surface px-3 py-2 text-sm text-error">
                  {streamError}
                </div>
              )}
              <ChatInput onSend={handleSendMessage} disabled={isStreaming} />
            </>
          ) : (
            <div className="flex flex-1 items-center justify-center">
              <div className="text-center">
                <MessageSquare size={48} className="mx-auto mb-4 text-subtle" />
                <p className="font-serif text-lg text-muted">
                  {t("selectOrCreate")}
                </p>
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
