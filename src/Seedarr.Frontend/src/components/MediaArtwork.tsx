import React, { useState, useEffect } from "react";

export interface MediaArtworkProps {
  src?: string | null;
  alt?: string;
  title?: string;
  category?: string;
  className?: string;
  style?: React.CSSProperties;
  loading?: "lazy" | "eager";
  width?: number | string;
  height?: number | string;
  aspectRatio?: string;
  borderRadius?: string;
  fallbackIcon?: string;
  allowRetry?: boolean;
  onRetry?: () => void;
  onError?: () => void;
  initialState?: MediaArtworkState;
}

export type MediaArtworkState = "loading" | "loaded" | "error";

export function sanitizeArtworkUrl(url?: string | null): string | null {
  if (!url) return null;
  const trimmed = url.trim();
  if (!trimmed || trimmed === "null" || trimmed === "undefined") {
    return null;
  }
  return trimmed;
}

export function getCategoryGlyph(category?: string): string {
  if (!category) return "📦";
  const cat = category.toLowerCase();
  if (
    cat.includes("movie") ||
    cat.includes("radarr") ||
    cat.includes("film") ||
    cat.includes("cinema")
  ) {
    return "🎬";
  }
  if (
    cat.includes("tv") ||
    cat.includes("sonarr") ||
    cat.includes("series") ||
    cat.includes("show") ||
    cat.includes("episode")
  ) {
    return "📺";
  }
  if (
    cat.includes("music") ||
    cat.includes("lidarr") ||
    cat.includes("audio") ||
    cat.includes("song") ||
    cat.includes("album")
  ) {
    return "🎵";
  }
  if (cat.includes("book") || cat.includes("readarr")) {
    return "📚";
  }
  if (cat.includes("anime")) {
    return "🍙";
  }
  if (cat.includes("game") || cat.includes("gaming")) {
    return "🎮";
  }
  if (cat.includes("software") || cat.includes("app") || cat.includes("iso")) {
    return "💾";
  }
  return "📦";
}

export function buildPlaceholderUrl(title?: string, category?: string): string {
  const params = new URLSearchParams();
  if (title) params.set("title", title);
  if (category) params.set("category", category);
  const qs = params.toString();
  return `/api/v1/mediacover/placeholder${qs ? `?${qs}` : ""}`;
}

export function MediaArtwork({
  src,
  alt = "",
  title,
  category,
  className = "",
  style = {},
  loading = "lazy",
  width,
  height,
  aspectRatio = "2 / 3",
  borderRadius,
  fallbackIcon,
  allowRetry = false,
  onRetry,
  onError,
  initialState,
}: MediaArtworkProps) {
  const placeholderUrl = buildPlaceholderUrl(title, category);
  const cleanSrc = sanitizeArtworkUrl(src);
  const initialSrc = cleanSrc || placeholderUrl;

  const [currentSrc, setCurrentSrc] = useState<string>(initialSrc);
  const [artworkState, setArtworkState] = useState<MediaArtworkState>(initialState || "loading");

  useEffect(() => {
    const nextCleanSrc = sanitizeArtworkUrl(src);
    const nextSrc = nextCleanSrc || placeholderUrl;
    setCurrentSrc(nextSrc);
    setArtworkState(initialState || "loading");
  }, [src, title, category, placeholderUrl, initialState]);

  const handleLoad = () => {
    setArtworkState("loaded");
  };

  const handleError = () => {
    if (currentSrc !== placeholderUrl) {
      // Primary src failed; attempt dynamic SVG placeholder
      setCurrentSrc(placeholderUrl);
      setArtworkState("loading");
    } else {
      // Even placeholder failed; render client-side CSS fallback
      setArtworkState("error");
      onError?.();
    }
  };

  const handleRetry = (e?: React.MouseEvent) => {
    e?.stopPropagation();
    const nextClean = sanitizeArtworkUrl(src);
    setCurrentSrc(nextClean || placeholderUrl);
    setArtworkState("loading");
    onRetry?.();
  };

  const numericWidth =
    typeof width === "number"
      ? width
      : typeof width === "string" && width.endsWith("px")
        ? parseInt(width, 10)
        : undefined;

  const isVerySmall = numericWidth !== undefined && numericWidth <= 30;
  const isSmall = (numericWidth !== undefined && numericWidth <= 60) || isVerySmall;

  const glyph = fallbackIcon || getCategoryGlyph(category);

  return (
    <div
      className={`media-artwork-container ${className}`}
      style={{
        position: "relative",
        width: width || "100%",
        height: height || (aspectRatio === "auto" ? "100%" : "auto"),
        aspectRatio: aspectRatio === "auto" ? undefined : aspectRatio,
        backgroundColor: "#141414",
        borderRadius,
        overflow: "hidden",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        ...style,
      }}
    >
      {/* Loading Skeleton Indicator */}
      {artworkState === "loading" && (
        <div
          className="skeleton"
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            width: "100%",
            height: "100%",
            borderRadius,
            zIndex: 1,
          }}
        />
      )}

      {/* Primary / SVG Placeholder Image */}
      {artworkState !== "error" ? (
        <img
          src={currentSrc}
          alt={alt || title || "Artwork"}
          loading={loading}
          onLoad={handleLoad}
          onError={handleError}
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            width: "100%",
            height: "100%",
            objectFit: "cover",
            borderRadius,
            opacity: artworkState === "loaded" ? 1 : 0,
            transition: "opacity 0.3s ease-in-out",
            zIndex: 2,
          }}
        />
      ) : (
        /* Styled CSS Fallback when image fails to load or src is absent */
        <div
          className="media-artwork-fallback"
          aria-label={alt || title || "Artwork placeholder"}
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            width: "100%",
            height: "100%",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            padding: isVerySmall ? "1px" : isSmall ? "0.2rem" : "0.75rem",
            textAlign: "center",
            background: "linear-gradient(180deg, #2a2620 0%, #151412 100%)",
            color: "var(--text-secondary, #9c9484)",
            borderRadius,
            zIndex: 2,
            boxSizing: "border-box",
            overflow: "hidden",
          }}
        >
          <span
            style={{
              fontSize: isVerySmall ? "0.85rem" : isSmall ? "1.2rem" : "2.2rem",
              marginBottom: isSmall ? 0 : "0.35rem",
              lineHeight: 1,
              userSelect: "none",
            }}
          >
            {glyph}
          </span>
          {!isSmall && title && (
            <div
              className="media-artwork-title"
              style={{
                fontSize: "0.78rem",
                fontWeight: 600,
                wordBreak: "break-word",
                lineHeight: 1.25,
                color: "var(--text-secondary, #b0a898)",
                display: "-webkit-box",
                WebkitLineClamp: 2,
                WebkitBoxOrient: "vertical",
                overflow: "hidden",
              }}
            >
              {title}
            </div>
          )}
          {allowRetry && (
            <button
              type="button"
              className="media-artwork-retry-btn"
              onClick={handleRetry}
              title="Retry loading artwork"
              aria-label="Retry loading artwork"
              style={{
                marginTop: isVerySmall ? "1px" : isSmall ? "2px" : "6px",
                background: "rgba(255, 255, 255, 0.15)",
                border: "1px solid rgba(255, 255, 255, 0.25)",
                borderRadius: "3px",
                color: "var(--text-secondary, #b0a898)",
                fontSize: isVerySmall ? "0.6rem" : isSmall ? "0.7rem" : "0.8rem",
                cursor: "pointer",
                padding: isVerySmall ? "0 2px" : isSmall ? "1px 4px" : "2px 8px",
                lineHeight: 1,
              }}
            >
              ↻
            </button>
          )}
        </div>
      )}
    </div>
  );
}

export default MediaArtwork;
