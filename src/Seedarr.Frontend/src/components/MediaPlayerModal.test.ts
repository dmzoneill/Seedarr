import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  buildStreamUrl,
  buildDownloadUrl,
  buildExternalPlayerUrl,
  cleanUpMediaElement,
  setSubtitleTrackActive,
  getCodecErrorMessage,
} from "../utils/mediaPlayer";
import type { SubtitleTrack, Torrent } from "../api/types";

describe("MediaPlayerModal: Core Component Contract & Behaviors", () => {
  const mockTorrent: Pick<Torrent, "id" | "name" | "mediaTitle"> = {
    id: 10,
    name: "Big.Buck.Bunny.1080p.HEVC.mkv",
    mediaTitle: "Big Buck Bunny",
  };

  const mockVideoFile = {
    id: 42,
    path: "Big.Buck.Bunny.1080p.HEVC.mkv",
    size: 276_000_000,
  };

  it("constructs correct stream URL and download URL for media player modal", () => {
    const stream = buildStreamUrl(mockTorrent.id, mockVideoFile.id);
    const download = buildDownloadUrl(mockTorrent.id, mockVideoFile.id);

    assert.equal(stream, "/api/v1/torrent/10/files/42/stream");
    assert.equal(download, "/api/v1/torrent/10/files/42/download");
  });

  it("constructs external player links (VLC, MPV) with full origin", () => {
    const stream = buildStreamUrl(mockTorrent.id, mockVideoFile.id);
    const vlc = buildExternalPlayerUrl("vlc", stream, "http://localhost:5000");
    const mpv = buildExternalPlayerUrl("mpv", stream, "http://localhost:5000");

    assert.equal(
      vlc,
      "vlc://http://localhost:5000/api/v1/torrent/10/files/42/stream",
    );
    assert.equal(
      mpv,
      "web+mpv://http://localhost:5000/api/v1/torrent/10/files/42/stream",
    );
  });

  it("cleans up media pipeline on unmount (pause, remove src, load)", () => {
    const calls: string[] = [];
    const mediaEl: any = {
      pause() {
        calls.push("pause");
      },
      removeAttribute(attr: string) {
        calls.push(`removeAttribute:${attr}`);
      },
      removeChild() {},
      firstChild: null,
      load() {
        calls.push("load");
      },
    };

    cleanUpMediaElement(mediaEl);

    assert.deepEqual(calls, ["pause", "removeAttribute:src", "load"]);
  });

  it("handles codec errors with MEDIA_ERR_SRC_NOT_SUPPORTED code 4 by providing descriptive error and player fallbacks", () => {
    const errMessage = getCodecErrorMessage(4, mockVideoFile.path);

    assert.ok(
      errMessage.includes("cannot decode this media codec"),
      "Should explain browser decode failure",
    );
    assert.ok(
      errMessage.includes("HEVC / H.265 video"),
      "Should identify HEVC video",
    );
    assert.ok(
      errMessage.includes("Matroska (.mkv) container"),
      "Should identify MKV container",
    );
  });

  it("dynamically manages multiple subtitle tracks and toggles track modes", () => {
    const tracks: any = [
      { mode: "disabled", label: "English", language: "en" },
      { mode: "disabled", label: "Spanish", language: "es" },
    ];

    const subtitles: SubtitleTrack[] = [
      {
        trackId: 1,
        title: "English",
        language: "English",
        twoLetterCode: "en",
        format: "vtt",
        path: "subs/en.vtt",
        isExternal: false,
        isForced: false,
        isHearingImpaired: false,
        isDefault: true,
        url: "/api/v1/torrent/10/files/42/subtitles/1.vtt",
      },
      {
        trackId: 2,
        title: "Spanish",
        language: "Spanish",
        twoLetterCode: "es",
        format: "vtt",
        path: "subs/es.vtt",
        isExternal: true,
        isForced: false,
        isHearingImpaired: false,
        isDefault: false,
        url: "/api/v1/torrent/10/files/42/subtitles/2.vtt",
      },
    ];

    // Select Spanish
    setSubtitleTrackActive(tracks, 2, subtitles);
    assert.equal(tracks[0].mode, "disabled");
    assert.equal(tracks[1].mode, "showing");

    // Turn off
    setSubtitleTrackActive(tracks, "off", subtitles);
    assert.equal(tracks[0].mode, "disabled");
    assert.equal(tracks[1].mode, "disabled");
  });
});
