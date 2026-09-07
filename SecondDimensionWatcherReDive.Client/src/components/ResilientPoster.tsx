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
  const source = src ?? null;
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [retryState, setRetryState] = React.useState({ source, attempt: 0 });
  const [imageState, setImageState] = React.useState<{
    source: string | null;
    state: ImageState;
  }>({ source, state: source ? "loading" : "missing" });
  const [authenticatedImage, setAuthenticatedImage] = React.useState<{
    requestUrl: string;
    objectUrl: string;
  } | null>(null);
  const [visibleSource, setVisibleSource] = React.useState<string | null>(
    eager ? source : null,
  );
  const attempt = retryState.source === source ? retryState.attempt : 0;
  const state =
    imageState.source === source
      ? imageState.state
      : source
        ? "loading"
        : "missing";

  React.useEffect(() => {
    setRetryState({ source, attempt: 0 });
    setImageState({ source, state: source ? "loading" : "missing" });
  }, [source]);

  const requestUrl = React.useMemo(() => {
    if (!source) return null;
    if (attempt === 0) return source;
    const separator = source.includes("?") ? "&" : "?";
    return `${source}${separator}retry=${attempt}`;
  }, [attempt, source]);

  const requiresAuthentication =
    requestUrl?.startsWith("/api/images/tmdb/") ?? false;
  const shouldLoad =
    !requiresAuthentication || eager || visibleSource === source;

  React.useEffect(() => {
    if (!source || !requiresAuthentication || shouldLoad) return;
    const element = containerRef.current;
    if (!element || !("IntersectionObserver" in window)) {
      setVisibleSource(source);
      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((entry) => entry.isIntersecting)) return;
        setVisibleSource(source);
        observer.disconnect();
      },
      { rootMargin: "256px" },
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [requiresAuthentication, shouldLoad, source]);

  const retry = React.useCallback(() => {
    setImageState({ source, state: "loading" });
    setRetryState((current) => ({
      source,
      attempt: current.source === source ? current.attempt + 1 : 1,
    }));
  }, [source]);

  const handleError = React.useCallback(() => {
    // Retry one transient failure automatically with a distinct browser cache
    // key, then leave a stable placeholder instead of a broken-image glyph.
    if (attempt === 0) retry();
    else setImageState({ source, state: "error" });
  }, [attempt, retry, source]);

  React.useEffect(() => {
    setAuthenticatedImage(null);
    if (!requestUrl || !requiresAuthentication || !shouldLoad) return;

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
        setAuthenticatedImage({ requestUrl, objectUrl });
      })
      .catch(() => {
        if (abortController.signal.aborted) return;
        handleError();
      });

    return () => {
      abortController.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [handleError, requestUrl, requiresAuthentication, shouldLoad]);

  const imageUrl = requiresAuthentication
    ? authenticatedImage?.requestUrl === requestUrl
      ? authenticatedImage.objectUrl
      : null
    : requestUrl;

  const fallbackLabel = alt
    ? t("images.unavailableFor", { name: alt })
    : t("images.unavailable");

  return (
    <div
      ref={containerRef}
      className={cn(
        "relative isolate flex shrink-0 items-center justify-center overflow-hidden bg-canvas text-subtle",
        className,
      )}
      aria-busy={state === "loading" || undefined}
      data-image-state={state}
    >
      <ImageOff size={22} aria-hidden="true" />
      {(state === "missing" || state === "error") && alt ? (
        <span className="sr-only" role="img" aria-label={fallbackLabel} />
      ) : null}
      {state === "loading" && !imageUrl && alt ? (
        <span className="sr-only" role="img" aria-label={alt} />
      ) : null}
      {state === "loading" ? (
        <span
          aria-hidden="true"
          className="absolute inset-0 animate-pulse bg-border-light/60"
        />
      ) : null}
      {imageUrl && state !== "error" ? (
        <img
          key={imageUrl}
          src={imageUrl}
          alt={alt}
          loading={eager ? "eager" : "lazy"}
          decoding="async"
          draggable={false}
          onLoad={() => setImageState({ source, state: "loaded" })}
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
