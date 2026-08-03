import { useState, useRef, useCallback } from "react";
import { useAddTorrent, AddTorrentResult } from "../api/hooks";

interface AddTorrentModalProps {
  onClose: () => void;
}

type InputMode = "file" | "magnet";

function AddTorrentModal({ onClose }: AddTorrentModalProps) {
  const [mode, setMode] = useState<InputMode>("file");
  const [files, setFiles] = useState<File[]>([]);
  const [magnetLink, setMagnetLink] = useState("");
  const [isDragOver, setIsDragOver] = useState(false);
  const [resultMessage, setResultMessage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const addTorrent = useAddTorrent();

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const addFiles = useCallback((incoming: FileList | File[]) => {
    const torrentFiles = Array.from(incoming).filter((f) =>
      f.name.endsWith(".torrent"),
    );
    if (torrentFiles.length === 0) return;
    setFiles((prev) => {
      const existing = new Set(prev.map((f) => f.name));
      const merged = [...prev];
      for (const f of torrentFiles) {
        if (!existing.has(f.name)) {
          merged.push(f);
          existing.add(f.name);
        }
      }
      return merged;
    });
  }, []);

  const removeFile = (name: string) => {
    setFiles((prev) => prev.filter((f) => f.name !== name));
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      addFiles(e.dataTransfer.files);
    },
    [addFiles],
  );

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      addFiles(e.target.files);
    }
    e.target.value = "";
  };

  const handleSubmit = () => {
    if (mode === "file" && files.length > 0) {
      setResultMessage(null);
      addTorrent.mutate(
        { files },
        {
          onSuccess: (result: AddTorrentResult) => {
            if (result.failed.length === 0) {
              onClose();
              return;
            }
            const failedNames = new Set(result.failed.map((f) => f.fileName));
            setFiles((prev) => prev.filter((f) => failedNames.has(f.name)));
            setResultMessage(
              `${result.added.length} added, ${result.failed.length} skipped: ${result.failed
                .map((f) => `${f.fileName} (${f.reason})`)
                .join("; ")}`,
            );
          },
        },
      );
    } else if (mode === "magnet" && magnetLink.trim()) {
      addTorrent.mutate(
        { magnetLink: magnetLink.trim() },
        { onSuccess: () => onClose() },
      );
    }
  };

  const canSubmit =
    (mode === "file" && files.length > 0) ||
    (mode === "magnet" && magnetLink.trim().startsWith("magnet:?"));

  return (
    <div className="modal-overlay" onClick={handleBackdropClick}>
      <div className="modal">
        <h2 className="modal-title">Add Torrent</h2>

        <div className="tab-nav">
          <button
            className={`tab-btn ${mode === "file" ? "tab-btn-active" : ""}`}
            onClick={() => setMode("file")}
          >
            Torrent File
          </button>
          <button
            className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
            onClick={() => setMode("magnet")}
          >
            Magnet Link
          </button>
        </div>

        {mode === "file" && (
          <>
            <div
              className={`drop-zone ${isDragOver ? "drop-zone-active" : ""} ${files.length > 0 ? "drop-zone-has-file" : ""}`}
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
            >
              {files.length > 0 ? (
                <span className="drop-zone-filename">
                  {files.length === 1
                    ? `${files[0].name} selected`
                    : `${files.length} torrents selected`}
                </span>
              ) : (
                <span className="drop-zone-prompt">
                  Drop .torrent files here or click to browse
                </span>
              )}
            </div>
            {files.length > 0 && (
              <ul className="add-torrent-file-list">
                {files.map((f) => (
                  <li key={f.name} className="add-torrent-file-item">
                    <span className="add-torrent-file-name">{f.name}</span>
                    <button
                      type="button"
                      className="add-torrent-file-remove"
                      onClick={() => removeFile(f.name)}
                      disabled={addTorrent.isPending}
                      aria-label={`Remove ${f.name}`}
                    >
                      ×
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <input
              ref={fileInputRef}
              type="file"
              accept=".torrent"
              multiple
              onChange={handleFileChange}
              style={{ display: "none" }}
            />
          </>
        )}

        {mode === "magnet" && (
          <input
            type="text"
            className="search-input modal-magnet-input"
            placeholder="magnet:?xt=urn:btih:..."
            value={magnetLink}
            onChange={(e) => setMagnetLink(e.target.value)}
            autoFocus
          />
        )}

        {(addTorrent.isError || resultMessage) && (
          <div className="modal-error">
            {addTorrent.isError
              ? addTorrent.error instanceof Error
                ? addTorrent.error.message
                : "Failed to add torrent"
              : resultMessage}
          </div>
        )}

        <div className="modal-actions">
          <button
            className="btn"
            onClick={onClose}
            disabled={addTorrent.isPending}
          >
            Cancel
          </button>
          <button
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
          >
            {addTorrent.isPending ? "Adding..." : "Add"}
          </button>
        </div>
      </div>
    </div>
  );
}

export default AddTorrentModal;
