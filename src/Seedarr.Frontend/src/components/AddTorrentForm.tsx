import React, { useState, useEffect, useMemo } from "react";
import { useTranslation } from "../i18n";
import {
  useAddTorrent,
  useCategories,
  AddTorrentResult,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { trackTorrentAdd } from "../utils/analytics";
import {
  TorrentFileInputTab,
  MagnetInputTab,
  IndexerSearchTab,
  TorrentCreationTab,
  buildExistingHashesSet,
  isReleaseInLibrary,
  isReleaseAdded,
  getReleaseButtonState,
  TORZNAB_CATEGORIES,
  sortReleases,
  paginateReleases,
  parseMagnetPreview,
  MagnetInfo,
} from "./addtorrent";

export interface AddTorrentFormProps {
  initialMode?: "file" | "magnet" | "search" | "create";
  initialQuery?: string;
  isModal?: boolean;
  onClose?: () => void;
  onSuccess?: () => void;
}

export type InputMode = "file" | "magnet" | "search" | "create";

export {
  buildExistingHashesSet,
  isReleaseInLibrary,
  isReleaseAdded,
  getReleaseButtonState,
  TORZNAB_CATEGORIES,
  sortReleases,
  paginateReleases,
  parseMagnetPreview,
};
export type { MagnetInfo as MagnetPreviewInfo };

export function AddTorrentForm({
  initialMode = "file",
  initialQuery = "",
  isModal = false,
  onClose,
  onSuccess,
}: AddTorrentFormProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();

  const [mode, setMode] = useState<InputMode>(initialMode);
  const [files, setFiles] = useState<File[]>([]);
  const [magnetLink, setMagnetLink] = useState("");
  const [resultMessage, setResultMessage] = useState<string | null>(null);

  // Ingestion Controls State
  const { data: categories } = useCategories();
  const [selectedCategory, setSelectedCategory] = useState<string>("");
  const [customSavePath, setCustomSavePath] = useState<string>("");
  const [startPaused, setStartPaused] = useState<boolean>(false);
  const [sequentialDownload, setSequentialDownload] = useState<boolean>(false);
  const [firstLastPiecePrio, setFirstLastPiecePrio] = useState<boolean>(false);

  const activeCategoryObj = useMemo(
    () => categories?.find((c) => c.name === selectedCategory),
    [categories, selectedCategory],
  );

  // Preselect default category if none chosen
  useEffect(() => {
    if (!selectedCategory && categories && categories.length > 0) {
      const defaultCat = categories.find((c) => c.isDefault);
      if (defaultCat) {
        setSelectedCategory(defaultCat.name);
      }
    }
  }, [categories, selectedCategory]);

  const addTorrent = useAddTorrent();

  const isMagnetValid = magnetLink.trim().toLowerCase().startsWith("magnet:?");
  const canSubmit =
    (mode === "file" && files.length > 0) ||
    (mode === "magnet" && isMagnetValid);

  const handleSubmit = () => {
    if (mode === "file" && files.length > 0) {
      setResultMessage(null);
      addTorrent.mutate(
        {
          files,
          category: selectedCategory,
          savePath: customSavePath.trim() || undefined,
          paused: startPaused,
          sequentialDownload,
          firstLastPiecePrio,
        },
        {
          onSuccess: (result: AddTorrentResult) => {
            trackTorrentAdd("file", result?.added?.length || files.length, selectedCategory, {
              start_paused: startPaused,
              sequential: sequentialDownload,
            });
            if (result && result.failed && result.failed.length === 0) {
              showToast(
                t("addTorrent.addedTorrentsSuccess", {
                  count: result.added.length,
                  defaultValue: `Added ${result.added.length} torrent(s) successfully`,
                }),
                "success",
              );
              setFiles([]);
              if (onSuccess) onSuccess();
              if (onClose) onClose();
              return;
            }
            if (result && result.failed && result.failed.length > 0) {
              const failedNames = new Set(result.failed.map((f) => f.fileName));
              setFiles((prev) => prev.filter((f) => failedNames.has(f.name)));
              const details = result.failed
                .map((f) => `${f.fileName} (${f.reason})`)
                .join("; ");
              const summaryMsg = `${result.added?.length ?? 0} added, ${result.failed.length} skipped: ${details}`;
              setResultMessage(summaryMsg);
              showToast(summaryMsg, "error");
            } else {
              showToast(
                t("addTorrent.torrentAddedSuccess", "Torrent(s) added successfully"),
                "success",
              );
              setFiles([]);
              if (onSuccess) onSuccess();
              if (onClose) onClose();
            }
          },
          onError: (err) => {
            showToast(
              t("addTorrent.failedToUploadTorrents", {
                message: err.message,
                defaultValue: `Failed to upload torrents: ${err.message}`,
              }),
              "error",
            );
          },
        },
      );
    } else if (mode === "magnet" && magnetLink.trim()) {
      setResultMessage(null);
      addTorrent.mutate(
        {
          magnetLink: magnetLink.trim(),
          category: selectedCategory,
          savePath: customSavePath.trim() || undefined,
          paused: startPaused,
          sequentialDownload,
          firstLastPiecePrio,
        },
        {
          onSuccess: () => {
            trackTorrentAdd("magnet", 1, selectedCategory, {
              start_paused: startPaused,
              sequential: sequentialDownload,
            });
            showToast(
              t("addTorrent.magnetAddedSuccess", "Magnet link added successfully"),
              "success",
            );
            setMagnetLink("");
            if (onSuccess) onSuccess();
            if (onClose) onClose();
          },
          onError: (err) => {
            showToast(
              t("addTorrent.failedToAddMagnet", {
                message: err.message,
                defaultValue: `Failed to add magnet: ${err.message}`,
              }),
              "error",
            );
          },
        },
      );
    }
  };

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        flex: "1 1 auto",
        minHeight: 0,
        height: "100%",
        overflow: "hidden",
      }}
    >
      {/* Mode Switcher Tabs */}
      <div
        className="tab-nav"
        style={{
          display: "flex",
          gap: "0.5rem",
          marginBottom: "1.25rem",
          borderBottom: "1px solid var(--border-light)",
          paddingBottom: "0.5rem",
          flexShrink: 0,
        }}
      >
        <button
          type="button"
          className={`tab-btn ${mode === "file" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("file")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          {t("addTorrent.torrentFileTab", "📁 Torrent File")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("magnet")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          {t("addTorrent.magnetLinkTab", "🧲 Magnet Link")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "search" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("search")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          {t("addTorrent.indexerSearchTab", "🔍 Indexer Search")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "create" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("create")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          {t("addTorrent.createTorrentTab", "⚡ Create Torrent")}
        </button>
      </div>

      {/* Tab Panels */}
      <div
        style={{
          flex: "1 1 auto",
          minHeight: 0,
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
        }}
      >
        {mode === "file" && (
          <TorrentFileInputTab
            files={files}
            setFiles={setFiles}
            isModal={isModal}
          />
        )}

        {mode === "magnet" && (
          <MagnetInputTab
            magnetLink={magnetLink}
            setMagnetLink={setMagnetLink}
            isModal={isModal}
          />
        )}

        {mode === "search" && (
          <IndexerSearchTab
            initialQuery={initialQuery}
            selectedCategory={selectedCategory}
            isModal={isModal}
            onClose={onClose}
          />
        )}

        {mode === "create" && (
          <TorrentCreationTab
            isModal={isModal}
            onClose={onClose}
          />
        )}
      </div>

      {/* Ingestion & Speed Options (for File and Magnet modes) */}
      {(mode === "file" || mode === "magnet") && (
        <div
          style={{
            marginTop: "1rem",
            padding: "0.85rem 1rem",
            backgroundColor: "var(--bg-secondary, rgba(255,255,255,0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255,255,255,0.1))",
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
            flexShrink: 0,
          }}
        >
          <div
            style={{
              fontWeight: 600,
              fontSize: "0.85rem",
              color: "var(--text-primary)",
            }}
          >
            {t("addTorrent.ingestionOptions", "⚙️ Ingestion & Simulator Options")}
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "0.75rem",
            }}
          >
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.25rem",
              }}
            >
              <label
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 500,
                }}
              >
                {t("addTorrent.categoryLabel", "Category:")}
              </label>
              <select
                className="form-input"
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
                style={{
                  fontSize: "0.85rem",
                  padding: "0.4rem 0.6rem",
                  borderRadius: "4px",
                  background: "var(--bg-primary)",
                  color: "inherit",
                  border: "1px solid var(--border-light)",
                }}
              >
                <option value="">{t("addTorrent.noCategory", "(None)")}</option>
                {categories?.map((cat) => (
                  <option key={cat.id} value={cat.name}>
                    {cat.name} {cat.savePath ? `(${cat.savePath})` : ""}
                  </option>
                ))}
              </select>
            </div>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.25rem",
              }}
            >
              <label
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 500,
                }}
              >
                {t("addTorrent.savePathPrompt", "Save Path")}
              </label>
              <input
                type="text"
                className="form-input"
                placeholder={
                  activeCategoryObj?.savePath
                    ? `Default: ${activeCategoryObj.savePath}`
                    : "e.g. /downloads"
                }
                value={customSavePath}
                onChange={(e) => setCustomSavePath(e.target.value)}
                style={{
                  fontSize: "0.85rem",
                  padding: "0.4rem 0.6rem",
                  borderRadius: "4px",
                  background: "var(--bg-primary)",
                  color: "inherit",
                  border: "1px solid var(--border-light)",
                }}
              />
            </div>
          </div>

          <div
            style={{
              display: "flex",
              gap: "1.5rem",
              alignItems: "center",
              paddingTop: "0.25rem",
              flexWrap: "wrap",
            }}
          >
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                fontSize: "0.85rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <input
                type="checkbox"
                checked={startPaused}
                onChange={(e) => setStartPaused(e.target.checked)}
              />
              <span>⏸️ {t("addTorrent.startPaused", "Start Paused")}</span>
            </label>

            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                fontSize: "0.85rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <input
                type="checkbox"
                checked={sequentialDownload}
                onChange={(e) => setSequentialDownload(e.target.checked)}
              />
              <span>⏩ {t("torrents.sequentialDownload", "Sequential Download")}</span>
            </label>

            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                fontSize: "0.85rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <input
                type="checkbox"
                checked={firstLastPiecePrio}
                onChange={(e) => setFirstLastPiecePrio(e.target.checked)}
              />
              <span>🎯 {t("torrents.firstLastPiecePrio", "Prioritize First & Last Pieces")}</span>
            </label>
          </div>
        </div>
      )}

      {(addTorrent.isError || resultMessage) && (
        <div
          className="modal-error"
          style={{ marginTop: "1rem", borderRadius: "6px", flexShrink: 0 }}
        >
          {addTorrent.isError
            ? addTorrent.error instanceof Error
              ? addTorrent.error.message
              : "Failed to add torrent"
            : resultMessage}
        </div>
      )}

      {(mode === "file" || mode === "magnet") && (
        <div
          className="modal-actions"
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.5rem",
            marginTop: "1.25rem",
            flexShrink: 0,
          }}
        >
          {isModal && onClose && (
            <button
              type="button"
              className="btn btn-outline"
              onClick={onClose}
              disabled={addTorrent.isPending}
              style={{ borderRadius: "6px" }}
            >
              {t("common.cancel", "Cancel")}
            </button>
          )}
          <button
            type="button"
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
            style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
          >
            {addTorrent.isPending
              ? t("addTorrent.addingTorrent", "Adding...")
              : t("addTorrent.addTorrentBtn", "Add Torrent")}
          </button>
        </div>
      )}
    </div>
  );
}

export default AddTorrentForm;
