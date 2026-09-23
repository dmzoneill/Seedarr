import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
(globalThis as any).React = React;
import { renderToStaticMarkup } from "react-dom/server";
import {
  MediaArtwork,
  getCategoryGlyph,
  buildPlaceholderUrl,
  sanitizeArtworkUrl,
} from "./MediaArtwork";

describe("MediaArtwork: Helper Utilities", () => {
  describe("getCategoryGlyph", () => {
    it("returns 🎬 for movie and Radarr categories", () => {
      assert.equal(getCategoryGlyph("movie"), "🎬");
      assert.equal(getCategoryGlyph("Movies"), "🎬");
      assert.equal(getCategoryGlyph("Radarr"), "🎬");
      assert.equal(getCategoryGlyph("film-4k"), "🎬");
      assert.equal(getCategoryGlyph("cinema"), "🎬");
    });

    it("returns 📺 for TV and Sonarr categories", () => {
      assert.equal(getCategoryGlyph("tv"), "📺");
      assert.equal(getCategoryGlyph("Sonarr"), "📺");
      assert.equal(getCategoryGlyph("TV Series"), "📺");
      assert.equal(getCategoryGlyph("episode"), "📺");
      assert.equal(getCategoryGlyph("show"), "📺");
    });

    it("returns 🎵 for music and Lidarr categories", () => {
      assert.equal(getCategoryGlyph("music"), "🎵");
      assert.equal(getCategoryGlyph("Lidarr"), "🎵");
      assert.equal(getCategoryGlyph("audio"), "🎵");
      assert.equal(getCategoryGlyph("lossless album"), "🎵");
      assert.equal(getCategoryGlyph("song"), "🎵");
    });

    it("returns 📚 for book and Readarr categories", () => {
      assert.equal(getCategoryGlyph("book"), "📚");
      assert.equal(getCategoryGlyph("Readarr"), "📚");
      assert.equal(getCategoryGlyph("ebook"), "📚");
    });

    it("returns 🍙 for anime category", () => {
      assert.equal(getCategoryGlyph("anime"), "🍙");
      assert.equal(getCategoryGlyph("Anime-Sub"), "🍙");
    });

    it("returns 🎮 for gaming categories", () => {
      assert.equal(getCategoryGlyph("game"), "🎮");
      assert.equal(getCategoryGlyph("gaming"), "🎮");
      assert.equal(getCategoryGlyph("pc-games"), "🎮");
    });

    it("returns 💾 for software, app, and iso categories", () => {
      assert.equal(getCategoryGlyph("software"), "💾");
      assert.equal(getCategoryGlyph("app"), "💾");
      assert.equal(getCategoryGlyph("linux-iso"), "💾");
    });

    it("returns 📦 for undefined, empty, or unknown categories", () => {
      assert.equal(getCategoryGlyph(undefined), "📦");
      assert.equal(getCategoryGlyph(""), "📦");
      assert.equal(getCategoryGlyph("other"), "📦");
      assert.equal(getCategoryGlyph("unknown-misc"), "📦");
    });
  });

  describe("buildPlaceholderUrl", () => {
    it("builds URL with both title and category", () => {
      const url = buildPlaceholderUrl("Inception", "radarr");
      assert.equal(
        url,
        "/api/v1/mediacover/placeholder?title=Inception&category=radarr",
      );
    });

    it("properly encodes query parameters", () => {
      const url = buildPlaceholderUrl(
        "Breaking Bad & Better Call Saul",
        "TV Series / Sonarr",
      );
      assert.ok(url.startsWith("/api/v1/mediacover/placeholder?"));
      assert.ok(url.includes("title=Breaking+Bad+%26+Better+Call+Saul"));
      assert.ok(url.includes("category=TV+Series+%2F+Sonarr"));
    });

    it("builds URL with title only", () => {
      const url = buildPlaceholderUrl("Ubuntu 24.04");
      assert.equal(url, "/api/v1/mediacover/placeholder?title=Ubuntu+24.04");
    });

    it("builds URL with category only", () => {
      const url = buildPlaceholderUrl(undefined, "music");
      assert.equal(url, "/api/v1/mediacover/placeholder?category=music");
    });

    it("builds base endpoint when both title and category are missing", () => {
      const url = buildPlaceholderUrl();
      assert.equal(url, "/api/v1/mediacover/placeholder");
    });
  });

  describe("sanitizeArtworkUrl", () => {
    it("returns trimmed valid URL", () => {
      assert.equal(
        sanitizeArtworkUrl("  https://image.tmdb.org/t/p/w500/test.jpg  "),
        "https://image.tmdb.org/t/p/w500/test.jpg",
      );
      assert.equal(
        sanitizeArtworkUrl("/api/v1/mediacover/12/poster.jpg"),
        "/api/v1/mediacover/12/poster.jpg",
      );
    });

    it("returns null for null, undefined, empty, or whitespace strings", () => {
      assert.equal(sanitizeArtworkUrl(null), null);
      assert.equal(sanitizeArtworkUrl(undefined), null);
      assert.equal(sanitizeArtworkUrl(""), null);
      assert.equal(sanitizeArtworkUrl("   "), null);
    });

    it("returns null for literal string representations of null/undefined", () => {
      assert.equal(sanitizeArtworkUrl("null"), null);
      assert.equal(sanitizeArtworkUrl("undefined"), null);
      assert.equal(sanitizeArtworkUrl("  null  "), null);
    });
  });
});

describe("MediaArtwork: Component Rendering", () => {
  it("renders container with loading skeleton and image tag for valid src", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        src: "https://example.com/poster.jpg",
        alt: "The Matrix",
        title: "The Matrix",
        category: "radarr",
        width: "150px",
        height: "225px",
        borderRadius: "4px",
      }),
    );

    assert.ok(
      html.includes("media-artwork-container"),
      "Must have container class",
    );
    assert.ok(
      html.includes("skeleton"),
      "Must initially have loading skeleton",
    );
    assert.ok(html.includes("<img"), "Must render img element");
    assert.ok(
      html.includes('src="https://example.com/poster.jpg"'),
      "Img must have correct src",
    );
    assert.ok(
      html.includes('alt="The Matrix"'),
      "Img must have correct alt attribute",
    );
    assert.ok(
      html.includes("width:150px"),
      "Container must have correct width",
    );
    assert.ok(
      html.includes("height:225px"),
      "Container must have correct height",
    );
    assert.ok(
      html.includes("border-radius:4px"),
      "Container must have border-radius",
    );
  });

  it("renders dynamic SVG placeholder as initial src when src is null or absent", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        src: null,
        title: "Ubuntu Desktop",
        category: "software",
      }),
    );

    assert.ok(html.includes("media-artwork-container"));
    assert.ok(
      html.includes(
        "/api/v1/mediacover/placeholder?title=Ubuntu+Desktop&amp;category=software",
      ),
    );
  });

  it("renders 20x28 compact thumbnail for TorrentTable without layout distortion", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        src: "https://example.com/small.jpg",
        alt: "Table row poster",
        title: "Table row poster",
        category: "radarr",
        width: "20px",
        height: "28px",
        aspectRatio: "auto",
        borderRadius: "2px",
      }),
    );

    assert.ok(html.includes("width:20px"));
    assert.ok(html.includes("height:28px"));
    assert.ok(html.includes("border-radius:2px"));
    assert.ok(!html.includes("aspect-ratio:auto"));
  });

  it("renders 32x46 thumbnail for DetailsTab without layout distortion", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        src: "/api/v1/mediacover/5/poster.jpg",
        alt: "DetailsTab Poster",
        title: "DetailsTab Poster",
        category: "sonarr",
        width: "32px",
        height: "46px",
        aspectRatio: "auto",
        borderRadius: "3px",
      }),
    );

    assert.ok(html.includes("width:32px"));
    assert.ok(html.includes("height:46px"));
    assert.ok(html.includes("border-radius:3px"));
  });

  it("supports eager loading when specified", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        src: "https://example.com/eager.jpg",
        loading: "eager",
      }),
    );

    assert.ok(html.includes('loading="eager"'));
  });
});

describe("MediaArtwork: Fallback and Error Handling", () => {
  it("renders client-side CSS fallback container when state is error", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        src: "https://broken.invalid/poster.jpg",
        title: "Interstellar",
        category: "radarr",
        initialState: "error",
      }),
    );

    assert.ok(
      html.includes("media-artwork-fallback"),
      "Must render fallback container",
    );
    assert.ok(
      !html.includes("<img"),
      "Must NOT render broken img tag in error state",
    );
    assert.ok(html.includes("🎬"), "Must render category glyph for radarr");
    assert.ok(
      html.includes("Interstellar"),
      "Must render title in non-compact fallback",
    );
  });

  it("uses custom fallbackIcon when provided", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        title: "Special Item",
        category: "radarr",
        fallbackIcon: "⭐",
        initialState: "error",
      }),
    );

    assert.ok(html.includes("media-artwork-fallback"));
    assert.ok(html.includes("⭐"), "Must use custom fallbackIcon");
    assert.ok(
      !html.includes("🎬"),
      "Must not use default glyph when custom fallbackIcon is set",
    );
  });

  it("renders compact 20x28 fallback thumbnail without title overflow", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        title:
          "Very Long Torrent Name That Should Not Render In Table Thumbnail",
        category: "radarr",
        width: "20px",
        height: "28px",
        aspectRatio: "auto",
        borderRadius: "2px",
        initialState: "error",
      }),
    );

    assert.ok(html.includes("media-artwork-fallback"));
    assert.ok(html.includes("🎬"), "Must render glyph");
    assert.ok(
      html.includes("font-size:0.85rem"),
      "Must use compact font size for width <= 30",
    );
    assert.ok(
      !html.includes("media-artwork-title"),
      "Must NOT render title element in compact table thumbnail",
    );
    assert.ok(
      !html.includes(">Very Long Torrent Name"),
      "Must NOT render title text element in compact table thumbnail",
    );
  });

  it("renders retry button when allowRetry is enabled in error state", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        title: "Inception",
        category: "radarr",
        allowRetry: true,
        initialState: "error",
      }),
    );

    assert.ok(
      html.includes("media-artwork-retry-btn"),
      "Must render retry button",
    );
    assert.ok(html.includes('aria-label="Retry loading artwork"'));
    assert.ok(html.includes("↻"), "Must show retry icon");
  });

  it("does NOT render retry button when allowRetry is disabled", () => {
    const html = renderToStaticMarkup(
      React.createElement(MediaArtwork, {
        title: "Inception",
        category: "radarr",
        allowRetry: false,
        initialState: "error",
      }),
    );

    assert.ok(!html.includes("media-artwork-retry-btn"));
  });
});
