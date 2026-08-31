import React from "react";

import { RotateCcw } from "lucide-react";

import { Button } from "../components/ui/Button";

interface PlaybackErrorActionsProps {
  backLabel: string;
  retryLabel: string;
  showRetry: boolean;
  onBack: () => void;
  onRetry?: () => void;
}

export const reloadPlaybackLocation = (
  browserLocation: Pick<Location, "reload">,
): void => {
  browserLocation.reload();
};

export const reloadPlaybackPage = (): void => {
  reloadPlaybackLocation(window.location);
};

export const PlaybackErrorActions: React.FC<PlaybackErrorActionsProps> = ({
  backLabel,
  retryLabel,
  showRetry,
  onBack,
  onRetry = reloadPlaybackPage,
}) => (
  <div className="flex flex-wrap justify-center gap-3">
    {showRetry ? (
      <Button onClick={onRetry}>
        <RotateCcw size={16} />
        {retryLabel}
      </Button>
    ) : null}
    <Button variant={showRetry ? "outline" : "solid"} onClick={onBack}>
      {backLabel}
    </Button>
  </div>
);
