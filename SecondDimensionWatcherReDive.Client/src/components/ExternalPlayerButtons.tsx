import React from "react";
import { useTranslation } from "react-i18next";

import { ExternalLink } from "lucide-react";

import { Button } from "./ui/Button";

interface ExternalPlayer {
  name: string;
  buildUrl: (absoluteUrl: string) => string;
}

const PLAYERS: ExternalPlayer[] = [
  {
    name: "VLC",
    buildUrl: (url) => `vlc://${url}`,
  },
  {
    name: "PotPlayer",
    buildUrl: (url) => `potplayer://${url}`,
  },
  {
    name: "IINA",
    buildUrl: (url) => `iina://weblink?url=${encodeURIComponent(url)}`,
  },
  {
    name: "mpv",
    buildUrl: (url) => `mpv://${url}`,
  },
  {
    name: "nPlayer",
    buildUrl: (url) => `nplayer-${url}`,
  },
];

interface ExternalPlayerButtonsProps {
  playbackUrl: string;
}

export const ExternalPlayerButtons: React.FC<ExternalPlayerButtonsProps> = ({
  playbackUrl,
}) => {
  const { t } = useTranslation("player");
  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-xs text-muted">{t("openInExternal")}</span>
      {PLAYERS.map((player) => (
        <Button
          key={player.name}
          variant="outline"
          size="sm"
          onClick={() => {
            window.location.href = player.buildUrl(playbackUrl);
          }}
        >
          <ExternalLink size={14} />
          {player.name}
        </Button>
      ))}
    </div>
  );
};
