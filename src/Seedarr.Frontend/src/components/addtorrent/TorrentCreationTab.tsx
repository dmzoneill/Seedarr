import React, { useState } from "react";
import { useTranslation } from "../../i18n";
import { formatBytes } from "../../utils/formatters";
import { useToast } from "../../context/ToastContext";
import { api } from "../../api/client";
import type { TorrentCreationResult } from "../../api/types";

export interface TorrentCreationTabProps {
  isModal?: boolean;
  onClose?: () => void;
}

export function TorrentCreationTab({
  isModal = false,
  onClose,
}: TorrentCreationTabProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();

  const [createPath, setCreatePath] = useState("");
  const [createName, setCreateName] = useState("");
  const [createComment, setCreateComment] = useState("");
  const [createCreatedBy, setCreateCreatedBy] = useState("Seedarr");
  const [createPieceLength, setCreatePieceLength] = useState(0);
  const [createIsPrivate, setCreateIsPrivate] = useState(false);
  const [createTrackers, setCreateTrackers] = useState("");
  const [createWebSeeds, setCreateWebSeeds] = useState("");
  const [createOutputPath, setCreateOutputPath] = useState("");
  const [isCreating, setIsCreating] = useState(false);
  const [createResult, setCreateResult] =
    useState<TorrentCreationResult | null>(null);

  const handleCreateTorrent = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createPath.trim()) {
      showToast(
        t(
          "addTorrent.sourcePathRequired",
          "Source path is required to create a torrent",
        ),
        "error",
      );
      return;
    }

    try {
      setIsCreating(true);
      setCreateResult(null);

      const trackersList = createTrackers
        .split("\n")
        .map((tr) => tr.trim())
        .filter((tr) => tr.length > 0);

      const webSeedsList = createWebSeeds
        .split("\n")
        .map((w) => w.trim())
        .filter((w) => w.length > 0);

      const res = await api.createTorrent({
        path: createPath.trim(),
        name: createName.trim() || undefined,
        comment: createComment.trim() || undefined,
        createdBy: createCreatedBy.trim() || undefined,
        isPrivate: createIsPrivate,
        pieceLength: createPieceLength > 0 ? createPieceLength : undefined,
        trackers: trackersList.length > 0 ? trackersList : undefined,
        webSeeds: webSeedsList.length > 0 ? webSeedsList : undefined,
        outputPath: createOutputPath.trim() || undefined,
      });

      setCreateResult(res);
      if (res.success) {
        showToast(
          t(
            "addTorrent.torrentCreatedSuccess",
            "Torrent created successfully!",
          ),
          "success",
        );
      } else {
        showToast(
          res.errorMessage ||
            t("addTorrent.failedToCreateTorrent", "Failed to create torrent"),
          "error",
        );
      }
    } catch (err: any) {
      showToast(
        err?.message ||
          t("addTorrent.failedToCreateTorrent", "Failed to create torrent"),
        "error",
      );
    } finally {
      setIsCreating(false);
    }
  };

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        flex: "1 1 auto",
        minHeight: 0,
        overflowY: "auto",
        paddingRight: "0.5rem",
        gap: "1rem",
      }}
    >
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: "1rem",
        }}
      >
        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.3rem",
              color: "var(--text-secondary)",
            }}
          >
            {t(
              "addTorrent.sourcePathLabel",
              "Source Path (File or Directory) *",
            )}
          </label>
          <input
            type="text"
            value={createPath}
            onChange={(e) => setCreatePath(e.target.value)}
            placeholder={t(
              "addTorrent.sourcePathPlaceholder",
              "/downloads/complete/MyMovie or /data/file.iso",
            )}
            className="form-input"
            style={{
              width: "100%",
              padding: "0.5rem 0.75rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
            }}
          />
        </div>

        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.3rem",
              color: "var(--text-secondary)",
            }}
          >
            {t("addTorrent.torrentNameLabel", "Torrent Name (Optional)")}
          </label>
          <input
            type="text"
            value={createName}
            onChange={(e) => setCreateName(e.target.value)}
            placeholder={t(
              "addTorrent.torrentNamePlaceholder",
              "Defaults to source directory or filename",
            )}
            className="form-input"
            style={{
              width: "100%",
              padding: "0.5rem 0.75rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
            }}
          />
        </div>
      </div>

      <div>
        <label
          style={{
            display: "block",
            fontSize: "0.85rem",
            fontWeight: 600,
            marginBottom: "0.3rem",
            color: "var(--text-secondary)",
          }}
        >
          {t("addTorrent.commentLabel", "Comment (Optional)")}
        </label>
        <input
          type="text"
          value={createComment}
          onChange={(e) => setCreateComment(e.target.value)}
          placeholder={t(
            "addTorrent.commentPlaceholder",
            "Optional comment embedded in .torrent",
          )}
          className="form-input"
          style={{
            width: "100%",
            padding: "0.5rem 0.75rem",
            fontSize: "0.85rem",
            borderRadius: "6px",
            border: "1px solid var(--border-light)",
            backgroundColor: "var(--bg-primary, #10111a)",
            color: "inherit",
          }}
        />
      </div>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr 1fr",
          gap: "1rem",
        }}
      >
        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.3rem",
              color: "var(--text-secondary)",
            }}
          >
            {t("addTorrent.pieceSizeLabel", "Piece Size")}
          </label>
          <select
            value={createPieceLength}
            onChange={(e) => setCreatePieceLength(Number(e.target.value))}
            className="form-input"
            style={{
              width: "100%",
              padding: "0.5rem 0.75rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
            }}
          >
            <option value={0}>Automatic (Recommended)</option>
            <option value={16384}>16 KB</option>
            <option value={32768}>32 KB</option>
            <option value={65536}>64 KB</option>
            <option value={131072}>128 KB</option>
            <option value={262144}>256 KB</option>
            <option value={524288}>512 KB</option>
            <option value={1048576}>1 MB</option>
            <option value={2097152}>2 MB</option>
            <option value={4194304}>4 MB</option>
            <option value={8388608}>8 MB</option>
            <option value={16777216}>16 MB</option>
            <option value={33554432}>32 MB</option>
          </select>
        </div>

        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.3rem",
              color: "var(--text-secondary)",
            }}
          >
            {t("addTorrent.createdByLabel", "Created By")}
          </label>
          <input
            type="text"
            value={createCreatedBy}
            onChange={(e) => setCreateCreatedBy(e.target.value)}
            className="form-input"
            style={{
              width: "100%",
              padding: "0.5rem 0.75rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
            }}
          />
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            paddingTop: "1.2rem",
          }}
        >
          <label
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.5rem",
              fontSize: "0.85rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={createIsPrivate}
              onChange={(e) => setCreateIsPrivate(e.target.checked)}
            />
            <span style={{ fontWeight: 600 }}>
              {t(
                "addTorrent.privateTorrentLabel",
                "Private Torrent (No DHT / PEX)",
              )}
            </span>
          </label>
        </div>
      </div>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: "1rem",
        }}
      >
        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.3rem",
              color: "var(--text-secondary)",
            }}
          >
            {t(
              "addTorrent.trackersLabelWithTiers",
              "Tracker URLs (One per line, tiers separated by empty line)",
            )}
          </label>
          <textarea
            rows={3}
            value={createTrackers}
            onChange={(e) => setCreateTrackers(e.target.value)}
            placeholder="http://tracker.example.com/announce&#10;udp://tracker.openbittorrent.com:80/announce"
            className="form-control"
            style={{
              width: "100%",
              padding: "0.5rem 0.75rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
              resize: "vertical",
            }}
          />
        </div>

        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.3rem",
              color: "var(--text-secondary)",
            }}
          >
            {t("addTorrent.webSeedsLabel", "Web Seed URLs (One per line)")}
          </label>
          <textarea
            rows={3}
            value={createWebSeeds}
            onChange={(e) => setCreateWebSeeds(e.target.value)}
            placeholder="https://example.com/files/"
            className="form-control"
            style={{
              width: "100%",
              padding: "0.5rem 0.75rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
              resize: "vertical",
            }}
          />
        </div>
      </div>

      <div>
        <label
          style={{
            display: "block",
            fontSize: "0.85rem",
            fontWeight: 600,
            marginBottom: "0.3rem",
            color: "var(--text-secondary)",
          }}
        >
          {t("addTorrent.outputPathLabel", "Output .torrent Save Path")}
        </label>
        <input
          type="text"
          value={createOutputPath}
          onChange={(e) => setCreateOutputPath(e.target.value)}
          placeholder={t(
            "addTorrent.outputPathPlaceholder",
            "Defaults to /downloads/filename.torrent",
          )}
          className="form-input"
          style={{
            width: "100%",
            padding: "0.5rem 0.75rem",
            fontSize: "0.85rem",
            borderRadius: "6px",
            border: "1px solid var(--border-light)",
            backgroundColor: "var(--bg-primary, #10111a)",
            color: "inherit",
          }}
        />
      </div>

      {createResult && (
        <div
          style={{
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            backgroundColor: createResult.success
              ? "rgba(34, 197, 94, 0.1)"
              : "rgba(239, 68, 68, 0.1)",
            border: `1px solid ${createResult.success ? "rgba(34, 197, 94, 0.3)" : "rgba(239, 68, 68, 0.3)"}`,
            fontSize: "0.85rem",
          }}
        >
          {createResult.success ? (
            <div>
              <div
                style={{
                  fontWeight: 600,
                  color: "#4ade80",
                  marginBottom: "0.3rem",
                }}
              >
                ✓{" "}
                {t(
                  "addTorrent.torrentCreatedSuccess",
                  "Torrent created successfully!",
                )}
              </div>
              {createResult.outputPath && (
                <div>
                  <span style={{ color: "var(--text-muted)" }}>Output: </span>
                  <code style={{ color: "var(--accent)" }}>
                    {createResult.outputPath}
                  </code>
                </div>
              )}
              {createResult.infoHash && (
                <div>
                  <span style={{ color: "var(--text-muted)" }}>Hash: </span>
                  <code style={{ color: "#60a5fa" }}>
                    {createResult.infoHash}
                  </code>
                </div>
              )}
              <div>
                <span style={{ color: "var(--text-muted)" }}>Size: </span>
                {formatBytes(createResult.totalSize)} ({createResult.pieceCount}{" "}
                pieces @ {formatBytes(createResult.pieceLength)})
              </div>
            </div>
          ) : (
            <div style={{ color: "#f87171" }}>
              ✕{" "}
              {createResult.errorMessage ||
                t(
                  "addTorrent.failedToCreateTorrent",
                  "Failed to create torrent",
                )}
            </div>
          )}
        </div>
      )}

      <div
        style={{
          display: "flex",
          justifyContent: "flex-end",
          gap: "0.5rem",
          marginTop: "0.5rem",
        }}
      >
        {isModal && onClose && (
          <button
            type="button"
            className="btn btn-outline"
            onClick={onClose}
            disabled={isCreating}
          >
            {t("common.cancel", "Cancel")}
          </button>
        )}
        <button
          type="button"
          className="btn btn-primary"
          onClick={handleCreateTorrent}
          disabled={isCreating || !createPath.trim()}
        >
          {isCreating
            ? t("addTorrent.creatingTorrent", "Creating .torrent...")
            : t("addTorrent.createTorrentBtn", "⚡ Create .torrent")}
        </button>
      </div>
    </div>
  );
}

export default TorrentCreationTab;
