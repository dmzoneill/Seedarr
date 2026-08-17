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
    updateTag.mutate(editing, {
      onSuccess: () => setEditing(null),
    });
  }

  function handleDelete(id: number) {
    deleteTag.mutate(id);
  }

  const tagList = tags ?? [];

  return (
    <div className="content-area">
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              Tags ({tagList.length})
            </h1>
            <span className="badge badge-primary">Metadata</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Organize and filter torrent swarms by custom labels and categories
          </div>
        </div>

        <button
          className="btn btn-primary btn-small"
          onClick={() => setShowAdd(true)}
        >
          + Add Tag
        </button>
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
          border: "1px solid rgba(255, 255, 255, 0.08)",
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
                              onClick={() => handleDelete(tag.id)}
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
    </div>
  );
}

export default Tags;
