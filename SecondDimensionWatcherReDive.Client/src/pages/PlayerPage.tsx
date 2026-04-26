import Artplayer from "artplayer";
import { AlertTriangle, ArrowLeft } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams, useSearchParams } from "react-router";

import { generatePlaybackLink } from "../file/utils";
import { ExternalPlayerButtons } from "../components/ExternalPlayerButtons";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { PageTemplate } from "./PageTemplate";

const BRAND_TERRACOTTA = "#c96442";

export const PlayerPage: React.FC = () => {
  const { t, i18n } = useTranslation("player");
  const { animationId } = useParams<{ animationId: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { addToast } = useToast();

  const file = searchParams.get("file") ?? undefined;
  const fileName = file
    ? (file.split("/").pop() ?? file)
    : t("unknownFile");

  const [playbackUrl, setPlaybackUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  const playerContainerRef = React.useRef<HTMLDivElement>(null);
  const artRef = React.useRef<Artplayer | null>(null);

  React.useEffect(() => {
    if (!animationId) {
      setError(t("missingId"));
      setLoading(false);
      return;
    }

    let cancelled = false;

    generatePlaybackLink(animationId, file)
      .then((result) => {
        if (cancelled) return;
        setPlaybackUrl(result.url);
      })
      .catch(() => {
        if (cancelled) return;
        setError(t("generateLinkFailed"));
        addToast({ title: t("generateLinkFailed"), color: "danger" });
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [animationId, file, addToast, t]);

  React.useEffect(() => {
    if (!playbackUrl || !playerContainerRef.current) return;

    const lng = i18n.resolvedLanguage ?? i18n.language;
    const artplayerLang = lng.toLowerCase().startsWith("zh")
      ? "zh-cn"
      : lng.toLowerCase().startsWith("ja")
      ? "ja"
      : "en";

    const art = new Artplayer({
      container: playerContainerRef.current,
      url: playbackUrl,
      lang: artplayerLang,
      autoplay: false,
      fullscreen: true,
      pip: true,
      playbackRate: true,
      aspectRatio: true,
      screenshot: true,
      setting: true,
      theme: BRAND_TERRACOTTA,
      volume: 0.8,
      muted: false,
      autoSize: true,
      autoMini: true,
      flip: true,
      miniProgressBar: true,
      lock: true,
      fastForward: true,
      autoPlayback: true,
      autoOrientation: true,
    });

    artRef.current = art;

    return () => {
      art.destroy(false);
      artRef.current = null;
    };
  }, [playbackUrl, i18n.resolvedLanguage, i18n.language]);

  const goBack = React.useCallback(() => {
    if (window.history.length > 1) {
      navigate(-1);
    } else {
      navigate("/downloaded");
    }
  }, [navigate]);

  return (
    <PageTemplate>
      <Button variant="ghost" size="sm" onClick={goBack} className="mb-4">
        <ArrowLeft size={16} />
        {t("back")}
      </Button>

      {loading ? (
        <div className="flex justify-center py-16">
          <Spinner size={32} />
        </div>
      ) : error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={t("playFailed")}
          body={<p>{error}</p>}
          actions={
            <Button onClick={goBack}>{t("back")}</Button>
          }
        />
      ) : playbackUrl ? (
        <>
          <div className="overflow-hidden rounded-2xl border border-border bg-dark-deep shadow-whisper">
            <div ref={playerContainerRef} className="aspect-video w-full" />
          </div>

          <div className="mt-4 rounded-md border border-border bg-surface p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-foreground">
                  {fileName}
                </p>
                <p className="mt-0.5 text-xs text-muted">
                  {t("useExternalHint")}
                </p>
              </div>
              <ExternalPlayerButtons playbackUrl={playbackUrl} />
            </div>
          </div>
        </>
      ) : null}
    </PageTemplate>
  );
};
