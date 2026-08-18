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

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">Tags</h1>
        <button className="btn btn-primary" onClick={() => setShowAdd(true)}>
          Add Tag
        </button>
      </div>

      {showAdd && (
        <div className="card" style={{ marginBottom: 16 }}>
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <input
              type="text"
              className="form-input"
              placeholder="Tag name"
              value={newLabel}
              onChange={(e) => setNewLabel(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleCreate()}
              autoFocus
            />
            <button
              className="btn btn-primary"
              onClick={handleCreate}
              disabled={createTag.isPending}
            >
              Save
            </button>
            <button
              className="btn btn-default"
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

      <div className="card">
        {isLoading ? (
          <p className="loading">Loading tags...</p>
        ) : isError ? (
          <p className="error">Failed to load tags.</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Tag</th>
                  <th className="torrent-table-th">Torrents</th>
                  <th className="torrent-table-th">Actions</th>
                </tr>
              </thead>
              <tbody>
                {(tags ?? []).length === 0 ? (
                  <tr>
                    <td colSpan={3} className="torrent-table-empty">
                      No tags defined
                    </td>
                  </tr>
                ) : (
                  (tags ?? []).map((tag) => (
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
                          />
                        ) : (
                          <span className="badge badge-info">{tag.label}</span>
                        )}
                      </td>
                      <td>{tagUsageCounts[tag.label] ?? 0}</td>
                      <td>
                        {editing?.id === tag.id ? (
                          <div style={{ display: "flex", gap: 4 }}>
                            <button
                              className="btn btn-sm btn-primary"
                              onClick={handleUpdate}
                            >
                              Save
                            </button>
                            <button
                              className="btn btn-sm btn-default"
                              onClick={() => setEditing(null)}
                            >
                              Cancel
                            </button>
                          </div>
                        ) : (
                          <div style={{ display: "flex", gap: 4 }}>
                            <button
                              className="btn btn-sm btn-default"
                              onClick={() => setEditing({ ...tag })}
                            >
                              Edit
                            </button>
                            <button
                              className="btn btn-sm btn-danger"
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
