import { ChevronDown } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";

import { AiModel } from "../../chat/types";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "../ui/DropdownMenu";

interface ModelPickerProps {
  models: AiModel[];
  selectedModel: string | null;
  onSelect: (modelId: string) => void;
}

export const ModelPicker: React.FC<ModelPickerProps> = ({
  models,
  selectedModel,
  onSelect,
}) => {
  const { t } = useTranslation("chat");
  const selected = models.find((m) => m.id === selectedModel);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button className="inline-flex items-center gap-1.5 rounded-md border border-border bg-surface px-3 py-1.5 text-sm text-muted hover:text-foreground transition-colors">
          <span className="max-w-[200px] truncate">
            {selected?.name ?? t("selectModel")}
          </span>
          <ChevronDown size={14} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="max-h-64 overflow-y-auto">
        {models.map((model) => (
          <DropdownMenuItem key={model.id} onSelect={() => onSelect(model.id)}>
            <div className="flex flex-col">
              <span className="text-sm">{model.name}</span>
              <span className="text-xs text-subtle">{model.provider}</span>
            </div>
          </DropdownMenuItem>
        ))}
        {models.length === 0 && (
          <div className="px-3 py-2 text-sm text-subtle">{t("noModels")}</div>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
};
