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
}

export type MediaArtworkState = "loading" | "loaded" | "error";

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
}: MediaArtworkProps) {
  const placeholderUrl = buildPlaceholderUrl(title, category);
  const initialSrc = src && src.trim().length > 0 ? src.trim() : placeholderUrl;

  const [currentSrc, setCurrentSrc] = useState<string>(initialSrc);
  const [artworkState, setArtworkState] = useState<MediaArtworkState>("loading");

  useEffect(() => {
    const nextSrc = src && src.trim().length > 0 ? src.trim() : placeholderUrl;
    setCurrentSrc(nextSrc);
    setArtworkState("loading");
  }, [src, title, category, placeholderUrl]);

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
    }
  };

  const isSmall =
    (typeof width === "number" && width <= 60) ||
    (typeof width === "string" && (width.endsWith("px") ? parseInt(width, 10) <= 60 : false));

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
            padding: isSmall ? "0.2rem" : "0.75rem",
            textAlign: "center",
            background: "linear-gradient(180deg, #2a2620 0%, #151412 100%)",
            color: "var(--text-secondary, #9c9484)",
            borderRadius,
            zIndex: 2,
            boxSizing: "border-box",
          }}
        >
          <span
            style={{
              fontSize: isSmall ? "1.2rem" : "2.2rem",
              marginBottom: isSmall ? 0 : "0.35rem",
              lineHeight: 1,
            }}
          >
            {glyph}
          </span>
          {!isSmall && title && (
            <div
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
        </div>
      )}
    </div>
  );
}

export default MediaArtwork;
