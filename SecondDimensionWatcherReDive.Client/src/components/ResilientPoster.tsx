import React from "react";
import { useTranslation } from "react-i18next";

import { ImageOff, RefreshCw } from "lucide-react";

import { authenticatedFetch } from "../auth/httpClient";
import { cn } from "../lib/cn";

type ImageState = "loading" | "loaded" | "missing" | "error";

export interface ResilientPosterProps {
  src: string | null | undefined;
  alt: string;
  className?: string;
  imageClassName?: string;
  allowManualRetry?: boolean;
  eager?: boolean;
}

export const ResilientPoster: React.FC<ResilientPosterProps> = ({
  src,
  alt,
  className,
  imageClassName,
  allowManualRetry = true,
  eager = false,
}) => {
  const { t } = useTranslation("common");
  const [attempt, setAttempt] = React.useState(0);
  const [state, setState] = React.useState<ImageState>(
    src ? "loading" : "missing",
  );
  const [authenticatedObjectUrl, setAuthenticatedObjectUrl] = React.useState<
    string | null
  >(null);

  React.useEffect(() => {
    setAttempt(0);
    setState(src ? "loading" : "missing");
  }, [src]);

  const requestUrl = React.useMemo(() => {
    if (!src) return null;
    if (attempt === 0) return src;
    const separator = src.includes("?") ? "&" : "?";
    return `${src}${separator}retry=${attempt}`;
  }, [attempt, src]);

  const requiresAuthentication =
    requestUrl?.startsWith("/api/images/tmdb/") ?? false;

  const retry = React.useCallback(() => {
    setState("loading");
    setAttempt((value) => value + 1);
  }, []);

  const handleError = React.useCallback(() => {
    // Retry one transient failure automatically with a distinct browser cache
    // key, then leave a stable placeholder instead of a broken-image glyph.
    if (attempt === 0) retry();
    else setState("error");
  }, [attempt, retry]);

  React.useEffect(() => {
    setAuthenticatedObjectUrl(null);
    if (!requestUrl || !requiresAuthentication) return;

    const abortController = new AbortController();
    let objectUrl: string | null = null;

    void authenticatedFetch(requestUrl, {
      signal: abortController.signal,
    })
      .then(async (response) => {
        if (!response.ok)
          throw new Error(`image request failed: ${response.status}`);
        const blob = await response.blob();
        if (!blob.type.startsWith("image/")) {
          throw new Error("image response has an invalid content type");
        }
        objectUrl = URL.createObjectURL(blob);
        if (abortController.signal.aborted) {
          URL.revokeObjectURL(objectUrl);
          objectUrl = null;
          return;
        }
        setAuthenticatedObjectUrl(objectUrl);
      })
      .catch(() => {
        if (abortController.signal.aborted) return;
        handleError();
      });

    return () => {
      abortController.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [handleError, requestUrl, requiresAuthentication]);

  const imageUrl = requiresAuthentication ? authenticatedObjectUrl : requestUrl;

  const fallbackLabel = alt
    ? t("images.unavailableFor", { name: alt })
    : t("images.unavailable");

  return (
    <div
      className={cn(
        "relative isolate flex shrink-0 items-center justify-center overflow-hidden bg-canvas text-subtle",
        className,
      )}
      aria-busy={state === "loading" || undefined}
      data-image-state={state}
      role={!requestUrl && alt ? "img" : undefined}
      aria-label={!requestUrl && alt ? fallbackLabel : undefined}
    >
      <ImageOff size={22} aria-hidden="true" />
      {state === "loading" ? (
        <span
          aria-hidden="true"
          className="absolute inset-0 animate-pulse bg-border-light/60"
        />
      ) : null}
      {imageUrl ? (
        <img
          key={imageUrl}
          src={imageUrl}
          alt={alt}
          loading={eager ? "eager" : "lazy"}
          decoding="async"
          draggable={false}
          onLoad={() => setState("loaded")}
          onError={handleError}
          className={cn(
            "absolute inset-0 h-full w-full object-cover transition-opacity duration-300",
            state === "loaded" ? "opacity-100" : "opacity-0",
            imageClassName,
          )}
        />
      ) : null}
      {state === "error" && allowManualRetry ? (
        <button
          type="button"
          aria-label={t("images.retry", { name: alt || t("images.poster") })}
          title={t("images.retry", { name: alt || t("images.poster") })}
          onClick={retry}
          className="absolute inset-0 flex items-center justify-center rounded-[inherit] bg-canvas/90 text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-inset focus:ring-focus"
        >
          <RefreshCw size={20} aria-hidden="true" />
        </button>
      ) : null}
    </div>
  );
};
