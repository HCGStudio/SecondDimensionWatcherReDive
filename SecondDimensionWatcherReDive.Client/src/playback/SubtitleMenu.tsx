import type Artplayer from "artplayer";
import React from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";

import * as DropdownMenu from "@radix-ui/react-dropdown-menu";

import { Captions, Check } from "lucide-react";

import type { ExternalSubtitle } from "./types";

export interface SubtitleMenuTarget {
  player: Artplayer;
  container: HTMLElement;
}

interface SubtitleMenuProps {
  target: SubtitleMenuTarget;
  subtitles: (ExternalSubtitle & { source: "external" | "embedded" })[];
  selectedSubtitle: string;
  offValue: string;
  discoveryComplete: boolean;
  skippedSubtitleCount: number;
  onSelect: (path: string) => void;
}

const itemClassName =
  "relative flex cursor-pointer select-none items-center gap-2 rounded px-3 py-2 text-sm text-white outline-none data-[highlighted]:bg-white/15";

export const SubtitleMenu: React.FC<SubtitleMenuProps> = ({
  target: { player, container },
  subtitles,
  selectedSubtitle,
  offValue,
  discoveryComplete,
  skippedSubtitleCount,
  onSelect,
}) => {
  const { t } = useTranslation("player");
  const [open, setOpen] = React.useState(false);
  const selected = subtitles.find((item) => item.path === selectedSubtitle);
  const currentLabel = selected?.label ?? t("tracks.off");

  React.useEffect(() => {
    if (!open || player.isDestroy) return;
    player.setting.show = false;
    player.controls.show = true;
    const keepControlsVisible = (visible: boolean) => {
      if (!visible) player.controls.show = true;
    };
    player.on("control", keepControlsVisible);
    return () => {
      player.off("control", keepControlsVisible);
    };
  }, [open, player]);

  // Keep menu keys from also seeking, changing volume or toggling playback.
  const onMenuKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Escape" && !open) return;
    event.stopPropagation();
    if (event.key === "Escape") {
      event.preventDefault();
      setOpen(false);
    }
  };

  return createPortal(
    <DropdownMenu.Root modal={false} open={open} onOpenChange={setOpen}>
      <DropdownMenu.Trigger asChild>
        <button
          type="button"
          aria-label={t("tracks.subtitleMenu")}
          title={`${t("tracks.subtitleMenu")}: ${currentLabel}`}
          className="flex h-full items-center justify-center gap-1 px-2 text-xs focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-white"
          style={{ color: selected ? "var(--art-theme)" : "white" }}
          onClick={(event) => event.stopPropagation()}
          onKeyDown={onMenuKeyDown}
        >
          <Captions size={20} aria-hidden="true" style={{ fill: "none" }} />
          <span className="hidden sm:inline">{t("tracks.subtitleMenu")}</span>
        </button>
      </DropdownMenu.Trigger>
      {/* Fullscreen only displays descendants of the player's root element. */}
      <DropdownMenu.Portal container={player.template.$player}>
        <DropdownMenu.Content
          aria-label={t("tracks.subtitle")}
          side="top"
          align="end"
          sideOffset={8}
          collisionBoundary={player.template.$player}
          collisionPadding={8}
          className="z-[100] w-72 max-w-[calc(100vw-2rem)] overflow-y-auto rounded-lg border border-white/15 bg-neutral-900/95 p-1 text-white shadow-xl"
          style={{
            maxHeight:
              "min(18rem, var(--radix-dropdown-menu-content-available-height))",
          }}
          onClick={(event) => event.stopPropagation()}
          onKeyDown={onMenuKeyDown}
          onEscapeKeyDown={(event) => {
            // Radix handles Escape during document capture, before Artplayer's
            // document listener can also exit web fullscreen.
            event.preventDefault();
            event.stopPropagation();
            setOpen(false);
          }}
        >
          <DropdownMenu.Label className="px-3 py-2 text-xs font-medium text-white/60">
            {t("tracks.subtitle")}
          </DropdownMenu.Label>
          <DropdownMenu.RadioGroup
            value={selectedSubtitle}
            onValueChange={onSelect}
          >
            <DropdownMenu.RadioItem value={offValue} className={itemClassName}>
              <span className="w-4 shrink-0">
                <DropdownMenu.ItemIndicator>
                  <Check
                    size={16}
                    aria-hidden="true"
                    style={{ fill: "none" }}
                  />
                </DropdownMenu.ItemIndicator>
              </span>
              {t("tracks.off")}
            </DropdownMenu.RadioItem>
            {subtitles.map((subtitle) => (
              <DropdownMenu.RadioItem
                key={subtitle.path}
                value={subtitle.path}
                textValue={subtitle.label}
                className={itemClassName}
              >
                <span className="w-4 shrink-0">
                  <DropdownMenu.ItemIndicator>
                    <Check
                      size={16}
                      aria-hidden="true"
                      style={{ fill: "none" }}
                    />
                  </DropdownMenu.ItemIndicator>
                </span>
                <span className="min-w-0 text-left">
                  <span className="block break-words">{subtitle.label}</span>
                  <span className="block text-xs text-white/60">
                    {[
                      subtitle.language,
                      t(
                        subtitle.source === "external"
                          ? "tracks.externalSubtitle"
                          : "tracks.embeddedSubtitle",
                      ),
                    ]
                      .filter(Boolean)
                      .join(" · ")}
                  </span>
                </span>
              </DropdownMenu.RadioItem>
            ))}
          </DropdownMenu.RadioGroup>
          {!discoveryComplete || subtitles.length === 0 ? (
            <p role="status" className="px-3 py-2 text-xs text-white/60">
              {t(discoveryComplete ? "tracks.none" : "tracks.loadingSubtitles")}
            </p>
          ) : null}
          {skippedSubtitleCount > 0 ? (
            <p className="px-3 py-2 text-xs text-amber-200">
              {t("mkv.bitmapSubtitlesSkipped", { count: skippedSubtitleCount })}
            </p>
          ) : null}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>,
    container,
  );
};
