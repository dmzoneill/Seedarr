import { useState, useMemo } from "react";
import {
  useTags,
  useCreateTag,
  useUpdateTag,
  useDeleteTag,
  useTorrents,
} from "../api/hooks";
import type { Tag } from "../api/types";

function Tags() {
  const { data: tags, isLoading, isError } = useTags();
  const { data: torrents } = useTorrents();
  const createTag = useCreateTag();
  const updateTag = useUpdateTag();
  const deleteTag = useDeleteTag();

  const [editing, setEditing] = useState<Tag | null>(null);
  const [newLabel, setNewLabel] = useState("");
  const [showAdd, setShowAdd] = useState(false);
  const [deletingTag, setDeletingTag] = useState<Tag | null>(null);

  const tagUsageCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    if (torrents) {
      for (const t of torrents) {
        if (t.label) counts[t.label] = (counts[t.label] ?? 0) + 1;
      }
    }
    return counts;
  }, [torrents]);

  function handleCreate() {
    if (!newLabel.trim()) return;
    createTag.mutate(
      { label: newLabel.trim() },
      {
        onSuccess: () => {
          setNewLabel("");
          setShowAdd(false);
        },
      },
    );
  }

  function handleUpdate() {
    if (!editing || !editing.label.trim()) return;
    updateTag.mutate(
      {
        ...editing,
        label: editing.label.trim(),
      },
      {
        onSuccess: () => setEditing(null),
      },
    );
  }

  function confirmDeleteTag() {
    if (!deletingTag) return;
    deleteTag.mutate(deletingTag.id, {
      onSuccess: () => setDeletingTag(null),
    });
  }

  const tagList = tags ?? [];

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🏷️</span> Tags ({tagList.length})
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Organize and filter torrent swarms by custom labels and categories
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <button
            className="btn btn-primary"
            onClick={() => setShowAdd(true)}
          >
            + Add Tag
          </button>
        </div>
      </div>

      {showAdd && (
        <div
          className="card"
          style={{
            marginBottom: "1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid rgba(200, 168, 78, 0.4)",
            padding: "1rem 1.25rem",
          }}
        >
          <div
            style={{
              display: "flex",
              gap: "0.75rem",
              alignItems: "center",
              maxWidth: "500px",
            }}
          >
            <input
              type="text"
              className="form-input"
              placeholder="Tag name (e.g. 4k-hdr, seedbox, anime)"
              value={newLabel}
              onChange={(e) => setNewLabel(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleCreate()}
              autoFocus
            />
            <button
              className="btn btn-primary btn-small"
              onClick={handleCreate}
              disabled={createTag.isPending || !newLabel.trim()}
            >
              {createTag.isPending ? "Saving..." : "Save Tag"}
            </button>
            <button
              className="btn btn-outline btn-small"
              onClick={() => {
                setShowAdd(false);
                setNewLabel("");
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      <div
        className="card"
        style={{
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: "1px solid var(--border-light)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        {isLoading ? (
          <p className="loading" style={{ padding: "1.5rem" }}>
            Loading tags...
          </p>
        ) : isError ? (
          <p className="error" style={{ padding: "1.5rem" }}>
            Failed to load tags.
          </p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Tag Label</th>
                  <th className="torrent-table-th">Assigned Torrents</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {tagList.length === 0 ? (
                  <tr>
                    <td colSpan={3} className="torrent-table-empty">
                      No tags defined yet. Click &quot;+ Add Tag&quot; to create
                      one.
                    </td>
                  </tr>
                ) : (
                  tagList.map((tag) => (
                    <tr key={tag.id} className="torrent-table-row">
                      <td>
                        {editing?.id === tag.id ? (
                          <input
                            type="text"
                            className="form-input"
                            value={editing.label}
                            onChange={(e) =>
                              setEditing({ ...editing, label: e.target.value })
                            }
                            onKeyDown={(e) =>
                              e.key === "Enter" && handleUpdate()
                            }
                            autoFocus
                            style={{ maxWidth: "250px" }}
                          />
                        ) : (
                          <span
                            className="badge badge-primary"
                            style={{
                              fontSize: "0.82rem",
                              padding: "0.2rem 0.6rem",
                            }}
                          >
                            🏷️ {tag.label}
                          </span>
                        )}
                      </td>
                      <td>
                        <span style={{ fontWeight: 600 }}>
                          {tagUsageCounts[tag.label] ?? 0}
                        </span>{" "}
                        <span
                          style={{
                            color: "var(--text-muted)",
                            fontSize: "0.8rem",
                          }}
                        >
                          torrents
                        </span>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {editing?.id === tag.id ? (
                          <div
                            style={{ display: "inline-flex", gap: "0.5rem" }}
                          >
                            <button
                              className="btn btn-primary btn-small"
                              onClick={handleUpdate}
                              disabled={updateTag.isPending}
                            >
                              Save
                            </button>
                            <button
                              className="btn btn-outline btn-small"
                              onClick={() => setEditing(null)}
                            >
                              Cancel
                            </button>
                          </div>
                        ) : (
                          <div
                            style={{ display: "inline-flex", gap: "0.5rem" }}
                          >
                            <button
                              className="btn btn-outline btn-small"
                              onClick={() => setEditing({ ...tag })}
                            >
                              Edit
                            </button>
                            <button
                              className="btn btn-danger btn-small"
                              onClick={() => setDeletingTag(tag)}
                            >
                              Delete
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Delete Confirmation Modal */}
      {deletingTag && (
        <div
          className="modal-overlay"
          onClick={() => setDeletingTag(null)}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 480,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "0.75rem" }}
            >
              Delete Tag: &quot;{deletingTag.label}&quot;
            </h3>

            <div
              style={{
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                backgroundColor: "rgba(220, 53, 69, 0.15)",
                border: "1px solid rgba(220, 53, 69, 0.35)",
                color: "var(--danger, #dc3545)",
                fontSize: "0.85rem",
                marginBottom: "1rem",
                lineHeight: 1.4,
              }}
            >
              ⚠️ Warning: Deleting this tag will remove it from all assigned torrents, indexers, and automated rules. This action cannot be undone.
            </div>

            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              Are you sure you want to delete tag <strong>{deletingTag.label}</strong>?
              {tagUsageCounts[deletingTag.label] ? (
                <>
                  <br />
                  Currently assigned to <strong>{tagUsageCounts[deletingTag.label]}</strong> {tagUsageCounts[deletingTag.label] === 1 ? "torrent" : "torrents"}.
                </>
              ) : (
                <>
                  <br />
                  This tag is not currently assigned to any torrents.
                </>
              )}
            </p>

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setDeletingTag(null)}
                disabled={deleteTag.isPending}
              >
                Cancel
              </button>
              <button
                className="btn btn-danger btn-small"
                onClick={confirmDeleteTag}
                disabled={deleteTag.isPending}
              >
                {deleteTag.isPending ? "Deleting..." : "Delete Tag"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Tags;
