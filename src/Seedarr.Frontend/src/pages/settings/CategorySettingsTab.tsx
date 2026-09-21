import { useState } from "react";
import {
  useCategories,
  useCreateCategory,
  useUpdateCategory,
  useDeleteCategory,
  useDiskSpace,
  useFileSystem,
} from "../../api/hooks";
import type { Category, DiskSpaceInfo } from "../../api/types";
import { formatBytes } from "../../utils/formatters";
import { useToast } from "../../context/ToastContext";
import { SectionCard, TextInput, NumberInput, Toggle } from "./shared";
import { trackCategoryAction } from "../../utils/analytics";

interface CategorySettingsProps {
  embedded?: boolean;
}

function getMatchingDisk(
  path: string | undefined | null,
  disks: DiskSpaceInfo[] | undefined,
): DiskSpaceInfo | null {
  if (!disks || disks.length === 0) return null;
  const target = (path || "").trim().replace(/\\/g, "/");
  if (!target) return null;

  const normalizedTarget = target.endsWith("/") ? target : `${target}/`;
  let bestMatch: DiskSpaceInfo | null = null;
  let bestMatchLength = -1;

  for (const d of disks) {
    if (!d.path) continue;
    const dPath = d.path.replace(/\\/g, "/");
    const normalizedDiskPath = dPath.endsWith("/") ? dPath : `${dPath}/`;
    if (
      normalizedTarget.startsWith(normalizedDiskPath) &&
      normalizedDiskPath.length > bestMatchLength
    ) {
      bestMatch = d;
      bestMatchLength = normalizedDiskPath.length;
    }
  }
  return bestMatch;
}

function DiskSpaceBadge({
  disk,
}: {
  disk: DiskSpaceInfo | null;
}) {
  if (!disk) {
    return (
      <div
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: "0.4rem",
          fontSize: "0.75rem",
          padding: "0.2rem 0.5rem",
          borderRadius: "4px",
          backgroundColor: "rgba(255, 255, 255, 0.05)",
          border: "1px solid rgba(255, 255, 255, 0.1)",
          color: "var(--text-muted, #94a3b8)",
        }}
        title="Storage volume is unmonitored or mount point is unmapped"
      >
        <span
          style={{
            width: 6,
            height: 6,
            borderRadius: "50%",
            backgroundColor: "var(--text-muted, #94a3b8)",
            display: "inline-block",
          }}
        />
        <span>Unmonitored / External</span>
      </div>
    );
  }

  const used = disk.totalSpace > 0 ? disk.totalSpace - disk.freeSpace : 0;
  const rawPct = disk.totalSpace > 0 ? (used / disk.totalSpace) * 100 : 0;
  const usedPct = Math.max(0, Math.min(100, rawPct));

  const color =
    usedPct >= 90
      ? "var(--danger, #ef4444)"
      : usedPct >= 75
        ? "var(--warning, #f59e0b)"
        : "var(--success, #22c55e)";

  return (
    <div
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "0.4rem",
        fontSize: "0.75rem",
        padding: "0.2rem 0.5rem",
        borderRadius: "4px",
        backgroundColor: "rgba(255, 255, 255, 0.05)",
        border: `1px solid ${color}40`,
        color: "var(--text-secondary, #cbd5e1)",
      }}
      title={`Mount: ${disk.path} (${disk.label || "Drive"}) - ${formatBytes(disk.freeSpace)} free of ${formatBytes(disk.totalSpace)}`}
    >
      <span
        style={{
          width: 6,
          height: 6,
          borderRadius: "50%",
          backgroundColor: color,
          display: "inline-block",
        }}
      />
      <span>
        <strong>{formatBytes(disk.freeSpace)}</strong> free /{" "}
        {formatBytes(disk.totalSpace)}
      </span>
      <span style={{ color: "var(--text-muted, #94a3b8)" }}>
        ({disk.label || disk.path})
      </span>
    </div>
  );
}

export function CategorySettingsTab({
  embedded: _embedded = false,
}: CategorySettingsProps) {
  const { data: categories, isLoading } = useCategories();
  const { data: diskSpace } = useDiskSpace();
  const createMutation = useCreateCategory();
  const updateMutation = useUpdateCategory();
  const deleteMutation = useDeleteCategory();

  const { showToast } = useToast();

  const [editingCategory, setEditingCategory] =
    useState<Partial<Category> | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);
  const [showFolderBrowser, setShowFolderBrowser] = useState(false);
  const [browsingPath, setBrowsingPath] = useState("");

  const {
    data: fsData,
    isLoading: isFsLoading,
    isError: isFsError,
    error: fsError,
  } = useFileSystem(showFolderBrowser ? browsingPath : undefined);

  const defaultCategoryForm: Partial<Category> = {
    name: "",
    savePath: "",
    defaultDownloadLimit: 0,
    defaultUploadLimit: 0,
    targetRatio: 0,
    targetSeedTimeMinutes: 0,
    autoStop: false,
    isDefault: false,
    maxActiveDownloads: null,
    maxActiveUploads: null,
    reservedDownloadSlots: 0,
  };

  const handleOpenAdd = () => {
    setModalError(null);
    setEditingCategory({ ...defaultCategoryForm });
  };

  const handleOpenEdit = (cat: Category) => {
    setModalError(null);
    setEditingCategory({
      id: cat.id,
      name: cat.name,
      savePath: cat.savePath || "",
      defaultDownloadLimit: cat.defaultDownloadLimit || 0,
      defaultUploadLimit: cat.defaultUploadLimit || 0,
      targetRatio: cat.targetRatio || 0,
      targetSeedTimeMinutes: cat.targetSeedTimeMinutes || 0,
      autoStop: Boolean(cat.autoStop),
      isDefault: Boolean(cat.isDefault),
      maxActiveDownloads: cat.maxActiveDownloads ?? null,
      maxActiveUploads: cat.maxActiveUploads ?? null,
      reservedDownloadSlots: cat.reservedDownloadSlots ?? 0,
    });
  };

  const handleOpenBrowser = (currentPath?: string) => {
    setBrowsingPath(
      currentPath ||
        editingCategory?.savePath ||
        diskSpace?.[0]?.path ||
        "/downloads",
    );
    setShowFolderBrowser(true);
  };

  const handleSelectBrowserPath = (path: string) => {
    if (editingCategory) {
      setEditingCategory({ ...editingCategory, savePath: path });
    }
    setShowFolderBrowser(false);
  };

  const handleSave = () => {
    if (!editingCategory) return;

    const trimmedName = editingCategory.name?.trim();
    if (!trimmedName) {
      setModalError("Category name is required");
      return;
    }

    setModalError(null);

    const payload: Partial<Category> = {
      name: trimmedName,
      savePath: editingCategory.savePath?.trim() || "",
      defaultDownloadLimit: Number(editingCategory.defaultDownloadLimit) || 0,
      defaultUploadLimit: Number(editingCategory.defaultUploadLimit) || 0,
      targetRatio: Number(editingCategory.targetRatio) || 0,
      targetSeedTimeMinutes: Number(editingCategory.targetSeedTimeMinutes) || 0,
      autoStop: Boolean(editingCategory.autoStop),
      isDefault: Boolean(editingCategory.isDefault),
      maxActiveDownloads:
        editingCategory.maxActiveDownloads && Number(editingCategory.maxActiveDownloads) > 0
          ? Number(editingCategory.maxActiveDownloads)
          : null,
      maxActiveUploads:
        editingCategory.maxActiveUploads && Number(editingCategory.maxActiveUploads) > 0
          ? Number(editingCategory.maxActiveUploads)
          : null,
      reservedDownloadSlots: Number(editingCategory.reservedDownloadSlots) || 0,
    };

    if (editingCategory.id) {
      updateMutation.mutate(
        { id: editingCategory.id, data: payload },
        {
          onSuccess: (updated) => {
            trackCategoryAction("update", Boolean(payload.savePath));
            showToast(
              `Category "${updated.name}" updated successfully`,
              "success",
            );
            setEditingCategory(null);
          },
          onError: (err: any) => {
            setModalError(err?.message || "Failed to update category");
          },
        },
      );
    } else {
      createMutation.mutate(payload, {
        onSuccess: (created) => {
          trackCategoryAction("create", Boolean(payload.savePath));
          showToast(
            `Category "${created.name}" created successfully`,
            "success",
          );
          setEditingCategory(null);
        },
        onError: (err: any) => {
          setModalError(err?.message || "Failed to create category");
        },
      });
    }
  };

  const handleDelete = (cat: Category) => {
    if (cat.isDefault) {
      showToast(
        "Cannot delete the default category. Designate another category as default first.",
        "error",
      );
      return;
    }

    if (
      !window.confirm(
        `Are you sure you want to delete category "${cat.name}"?`,
      )
    ) {
      return;
    }

    deleteMutation.mutate(cat.id, {
      onSuccess: () => {
        trackCategoryAction("delete");
        showToast(`Category "${cat.name}" deleted`, "info");
      },
      onError: (err: any) => {
        showToast(err?.message || "Failed to delete category", "error");
      },
    });
  };

  const isSaving = createMutation.isPending || updateMutation.isPending;
  const currentMatchingDisk = getMatchingDisk(
    editingCategory?.savePath,
    diskSpace,
  );

  return (
    <div id="category-settings-section">
      <SectionCard
        title="Categories"
        description="Organize torrents into categories with dedicated save paths, ratio goals, and speed limits"
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
            {categories
              ? `${categories.length} configured ${categories.length === 1 ? "category" : "categories"}`
              : "Loading categories..."}
          </div>

          <button
            type="button"
            className="btn btn-primary btn-small"
            onClick={handleOpenAdd}
            style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
          >
            <span>+</span> Add Category
          </button>
        </div>

        {isLoading ? (
          <div className="loading" style={{ padding: "1.5rem 0" }}>
            Loading categories...
          </div>
        ) : !categories || categories.length === 0 ? (
          <div
            style={{
              padding: "2.5rem 1.5rem",
              textAlign: "center",
              backgroundColor: "var(--bg-primary, #10111a)",
              borderRadius: "6px",
              border: "1px dashed var(--border-light)",
            }}
          >
            <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>🏷️</div>
            <h4 style={{ margin: "0 0 0.5rem", color: "var(--text-primary)" }}>
              No categories configured
            </h4>
            <p
              style={{
                margin: "0 auto 1.25rem",
                maxWidth: "460px",
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                lineHeight: 1.4,
              }}
            >
              Categories allow you to assign dedicated download directories
              (e.g. <code>/downloads/movies</code>, <code>/downloads/tv</code>),
              set speed limits, and manage torrent goals automatically.
            </p>
            <button
              type="button"
              className="btn btn-primary btn-small"
              onClick={handleOpenAdd}
            >
              + Create First Category
            </button>
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table
              className="table"
              style={{
                width: "100%",
                borderCollapse: "collapse",
                fontSize: "0.85rem",
              }}
            >
              <thead>
                <tr
                  style={{
                    borderBottom: "1px solid var(--border-light)",
                    textAlign: "left",
                    color: "var(--text-muted, #7e8092)",
                    fontSize: "0.8rem",
                  }}
                >
                  <th style={{ padding: "0.6rem 0.8rem" }}>Name</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Save Path & Disk Space</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Max Download</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Max Upload</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Slots</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Target Ratio</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Auto Stop</th>
                  <th style={{ padding: "0.6rem 0.8rem", textAlign: "right" }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {categories.map((cat) => {
                  const matchDisk = getMatchingDisk(cat.savePath, diskSpace);
                  return (
                    <tr
                      key={cat.id}
                      style={{
                        borderBottom: "1px solid var(--border-light)",
                      }}
                    >
                      <td style={{ padding: "0.65rem 0.8rem", fontWeight: 600 }}>
                        <div
                          style={{
                            display: "flex",
                            alignItems: "center",
                            gap: "0.5rem",
                          }}
                        >
                          <span>{cat.name}</span>
                          {cat.isDefault && (
                            <span
                              style={{
                                fontSize: "0.7rem",
                                padding: "0.15rem 0.45rem",
                                borderRadius: "3px",
                                backgroundColor: "rgba(59, 130, 246, 0.2)",
                                color: "var(--primary, #3b82f6)",
                                border: "1px solid rgba(59, 130, 246, 0.3)",
                              }}
                            >
                              Default
                            </span>
                          )}
                        </div>
                      </td>
                      <td
                        style={{
                          padding: "0.65rem 0.8rem",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            flexDirection: "column",
                            gap: "0.25rem",
                          }}
                        >
                          <span
                            style={{
                              fontFamily: "monospace",
                              color: cat.savePath
                                ? "var(--text-primary)"
                                : "var(--text-muted)",
                            }}
                          >
                            {cat.savePath || "Default Storage Path"}
                          </span>
                          <DiskSpaceBadge disk={matchDisk} />
                        </div>
                      </td>
                      <td style={{ padding: "0.65rem 0.8rem" }}>
                        {cat.defaultDownloadLimit
                          ? `${cat.defaultDownloadLimit} KB/s`
                          : "Unlimited"}
                      </td>
                      <td style={{ padding: "0.65rem 0.8rem" }}>
                        {cat.defaultUploadLimit
                          ? `${cat.defaultUploadLimit} KB/s`
                          : "Unlimited"}
                      </td>
                      <td style={{ padding: "0.65rem 0.8rem" }}>
                        <div
                          style={{
                            display: "flex",
                            flexDirection: "column",
                            gap: "0.15rem",
                            fontSize: "0.75rem",
                            color: "var(--text-secondary)",
                          }}
                        >
                          <span>
                            DL:{" "}
                            <strong>
                              {cat.maxActiveDownloads ? cat.maxActiveDownloads : "∞"}
                            </strong>
                          </span>
                          <span>
                            UL:{" "}
                            <strong>
                              {cat.maxActiveUploads ? cat.maxActiveUploads : "∞"}
                            </strong>
                          </span>
                          <span>
                            Reserved:{" "}
                            <strong>{cat.reservedDownloadSlots || 0}</strong>
                          </span>
                        </div>
                      </td>
                      <td style={{ padding: "0.65rem 0.8rem" }}>
                        {cat.targetRatio ? `${cat.targetRatio}x` : "Unlimited"}
                      </td>
                      <td style={{ padding: "0.65rem 0.8rem" }}>
                        {cat.autoStop ? (
                          <span
                            style={{
                              fontSize: "0.75rem",
                              color: "var(--success, #28a745)",
                              fontWeight: 600,
                            }}
                          >
                            Enabled
                          </span>
                        ) : (
                          <span style={{ color: "var(--text-muted)" }}>
                            Disabled
                          </span>
                        )}
                      </td>
                      <td
                        style={{ padding: "0.65rem 0.8rem", textAlign: "right" }}
                      >
                        <div
                          style={{
                            display: "inline-flex",
                            gap: "0.4rem",
                            justifyContent: "flex-end",
                          }}
                        >
                          <button
                            type="button"
                            className="btn btn-outline btn-small"
                            onClick={() => handleOpenEdit(cat)}
                            title={`Edit ${cat.name}`}
                            style={{
                              padding: "0.2rem 0.5rem",
                              fontSize: "0.75rem",
                            }}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="btn btn-danger btn-small"
                            onClick={() => handleDelete(cat)}
                            disabled={deleteMutation.isPending || cat.isDefault}
                            title={
                              cat.isDefault
                                ? "Cannot delete the default category. Designate another category as default first."
                                : `Delete ${cat.name}`
                            }
                            style={{
                              padding: "0.2rem 0.5rem",
                              fontSize: "0.75rem",
                              cursor: cat.isDefault ? "not-allowed" : "pointer",
                              opacity: cat.isDefault ? 0.5 : 1,
                            }}
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>

      {/* Add / Edit Category Modal */}
      {editingCategory && (
        <div className="modal-overlay" onClick={() => setEditingCategory(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 540,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <div
              className="modal-title"
              style={{
                fontSize: "1.2rem",
                marginBottom: "1rem",
                fontWeight: 600,
                color: "var(--text-primary)",
              }}
            >
              {editingCategory.id
                ? `Edit Category: ${editingCategory.name}`
                : "Add New Category"}
            </div>

            <TextInput
              label="Name"
              value={editingCategory.name || ""}
              onChange={(v) => {
                setEditingCategory({ ...editingCategory, name: v });
                setModalError(null);
              }}
              placeholder="e.g. movies, tv, music"
              hint="Unique name for this category"
            />

            <div style={{ marginBottom: "1rem" }}>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 500,
                  marginBottom: "0.35rem",
                  color: "var(--text-secondary)",
                }}
              >
                Save Path
              </label>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <input
                  type="text"
                  className="input"
                  value={editingCategory.savePath || ""}
                  onChange={(e) =>
                    setEditingCategory({
                      ...editingCategory,
                      savePath: e.target.value,
                    })
                  }
                  placeholder="/downloads/movies"
                  style={{
                    flex: 1,
                    padding: "0.5rem 0.75rem",
                    borderRadius: "6px",
                    border: "1px solid var(--border-light)",
                    backgroundColor: "var(--bg-primary, #0f111c)",
                    color: "var(--text-primary, #fff)",
                    fontSize: "0.85rem",
                  }}
                />
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={() => handleOpenBrowser(editingCategory.savePath)}
                  title="Browse folders / select directory"
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.4rem 0.75rem",
                    whiteSpace: "nowrap",
                  }}
                >
                  📁 Browse...
                </button>
              </div>

              {/* Disk Space Preview Badge */}
              <div style={{ marginTop: "0.4rem" }}>
                <DiskSpaceBadge disk={currentMatchingDisk} />
              </div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  marginTop: "0.25rem",
                }}
              >
                Custom download directory for torrents assigned to this category (optional)
              </div>
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: "1rem",
              }}
            >
              <NumberInput
                label="Max Download Speed"
                value={editingCategory.defaultDownloadLimit ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    defaultDownloadLimit: v,
                  })
                }
                min={0}
                suffix="KB/s"
                hint="0 for unlimited"
              />

              <NumberInput
                label="Max Upload Speed"
                value={editingCategory.defaultUploadLimit ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    defaultUploadLimit: v,
                  })
                }
                min={0}
                suffix="KB/s"
                hint="0 for unlimited"
              />
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: "1rem",
              }}
            >
              <NumberInput
                label="Max Active Downloads"
                value={editingCategory.maxActiveDownloads ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    maxActiveDownloads: v > 0 ? v : null,
                  })
                }
                min={0}
                hint="0 for unlimited concurrent downloads"
              />

              <NumberInput
                label="Max Active Uploads"
                value={editingCategory.maxActiveUploads ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    maxActiveUploads: v > 0 ? v : null,
                  })
                }
                min={0}
                hint="0 for unlimited concurrent uploads"
              />
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: "1rem",
              }}
            >
              <NumberInput
                label="Reserved Download Slots"
                value={editingCategory.reservedDownloadSlots ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    reservedDownloadSlots: v,
                  })
                }
                min={0}
                hint="Dedicated download slots reserved for this category"
              />
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: "1rem",
              }}
            >
              <NumberInput
                label="Target Ratio"
                value={editingCategory.targetRatio ?? 0}
                onChange={(v) =>
                  setEditingCategory({ ...editingCategory, targetRatio: v })
                }
                min={0}
                step={0.1}
                hint="Ratio goal before marking done (0 for none)"
              />

              <NumberInput
                label="Target Seeding Time"
                value={editingCategory.targetSeedTimeMinutes ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    targetSeedTimeMinutes: v,
                  })
                }
                min={0}
                suffix="min"
                hint="0 for unlimited"
              />
            </div>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
                marginTop: "0.5rem",
              }}
            >
              <Toggle
                label="Auto Stop When Goal Reached"
                checked={editingCategory.autoStop ?? false}
                onChange={(v) =>
                  setEditingCategory({ ...editingCategory, autoStop: v })
                }
                hint="Automatically pause/stop torrent once target ratio or seed time is met"
              />

              <Toggle
                label="Default Category"
                checked={editingCategory.isDefault ?? false}
                onChange={(v) =>
                  setEditingCategory({ ...editingCategory, isDefault: v })
                }
                hint="Automatically assign this category to newly added torrents without an explicit category"
              />
            </div>

            {modalError && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.6rem 0.8rem",
                  borderRadius: "6px",
                  fontSize: "0.85rem",
                  lineHeight: "1.4",
                  backgroundColor: "rgba(220, 53, 69, 0.15)",
                  color: "var(--danger, #dc3545)",
                  border: "1px solid rgba(220, 53, 69, 0.35)",
                }}
              >
                ✕ {modalError}
              </div>
            )}

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setEditingCategory(null)}
                disabled={isSaving}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSave}
                disabled={isSaving}
              >
                {isSaving
                  ? "Saving..."
                  : editingCategory.id
                    ? "Save Changes"
                    : "Create Category"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Folder Browser Modal */}
      {showFolderBrowser && (
        <div
          className="modal-overlay"
          onClick={() => setShowFolderBrowser(false)}
          role="dialog"
          aria-modal="true"
          aria-labelledby="folder-browser-title"
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            backdropFilter: "blur(6px)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 10000,
            padding: "1rem",
          }}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              width: "100%",
              maxWidth: 560,
              backgroundColor: "var(--bg-card, #161826)",
              borderRadius: "8px",
              padding: "1.5rem",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "1rem",
              }}
            >
              <h3
                id="folder-browser-title"
                style={{
                  margin: 0,
                  fontSize: "1.15rem",
                  fontWeight: 600,
                  color: "var(--text-primary, #fff)",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                📁 Select Category Folder
              </h3>
              <button
                type="button"
                onClick={() => setShowFolderBrowser(false)}
                style={{
                  background: "transparent",
                  border: "none",
                  color: "var(--text-muted)",
                  fontSize: "1.2rem",
                  cursor: "pointer",
                }}
              >
                ✕
              </button>
            </div>

            <div style={{ marginBottom: "1rem" }}>
              <label
                style={{
                  display: "block",
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.3rem",
                  fontWeight: 500,
                }}
              >
                Selected Directory Path
              </label>
              <div style={{ display: "flex", gap: "0.4rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={() => fsData?.parent && setBrowsingPath(fsData.parent)}
                  disabled={!fsData?.parent}
                  title={fsData?.parent ? `Go up to ${fsData.parent}` : "Root directory"}
                  style={{
                    padding: "0.4rem 0.6rem",
                    fontSize: "0.8rem",
                    whiteSpace: "nowrap",
                  }}
                >
                  ⬆ Up
                </button>
                <input
                  type="text"
                  className="input"
                  value={browsingPath}
                  onChange={(e) => setBrowsingPath(e.target.value)}
                  placeholder="/downloads/movies"
                  style={{
                    flex: 1,
                    padding: "0.5rem 0.75rem",
                    borderRadius: "6px",
                    border: "1px solid var(--border-light)",
                    backgroundColor: "var(--bg-primary, #0f111c)",
                    color: "var(--text-primary, #fff)",
                    fontFamily: "monospace",
                    fontSize: "0.85rem",
                    boxSizing: "border-box",
                  }}
                />
              </div>
              <div style={{ marginTop: "0.4rem" }}>
                <DiskSpaceBadge disk={getMatchingDisk(browsingPath, diskSpace)} />
              </div>
            </div>

            {/* Dynamic Filesystem Directory Listing */}
            <div style={{ marginBottom: "1rem" }}>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 600,
                  marginBottom: "0.4rem",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                }}
              >
                <span>Subdirectories in Current Path:</span>
                {isFsLoading && (
                  <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Loading...
                  </span>
                )}
              </div>
              <div
                style={{
                  border: "1px solid var(--border-light)",
                  borderRadius: "6px",
                  backgroundColor: "var(--bg-primary, #0f111c)",
                  maxHeight: "180px",
                  overflowY: "auto",
                  padding: "0.4rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.2rem",
                }}
              >
                {isFsLoading ? (
                  <div
                    style={{
                      padding: "1rem",
                      textAlign: "center",
                      color: "var(--text-muted)",
                      fontSize: "0.8rem",
                    }}
                  >
                    Fetching directory listing...
                  </div>
                ) : isFsError ? (
                  <div
                    style={{
                      padding: "0.75rem",
                      color: "var(--danger, #ef4444)",
                      fontSize: "0.8rem",
                    }}
                  >
                    ⚠️ {fsError?.message || "Failed to load directory contents."}
                  </div>
                ) : !fsData?.directories || fsData.directories.length === 0 ? (
                  <div
                    style={{
                      padding: "0.75rem",
                      textAlign: "center",
                      color: "var(--text-muted)",
                      fontSize: "0.8rem",
                    }}
                  >
                    No subdirectories found in this folder.
                  </div>
                ) : (
                  fsData.directories.map((dir, i) => (
                    <button
                      key={i}
                      type="button"
                      onClick={() => setBrowsingPath(dir.path)}
                      className="btn btn-outline"
                      style={{
                        textAlign: "left",
                        padding: "0.35rem 0.6rem",
                        fontSize: "0.8rem",
                        display: "flex",
                        alignItems: "center",
                        gap: "0.5rem",
                        border: "none",
                        backgroundColor: "transparent",
                        borderRadius: "4px",
                        cursor: "pointer",
                      }}
                      onMouseEnter={(e) => {
                        e.currentTarget.style.backgroundColor =
                          "rgba(255, 255, 255, 0.05)";
                      }}
                      onMouseLeave={(e) => {
                        e.currentTarget.style.backgroundColor = "transparent";
                      }}
                    >
                      <span style={{ fontSize: "0.9rem" }}>
                        {dir.type === "drive" ? "💾" : "📁"}
                      </span>
                      <span
                        style={{
                          flex: 1,
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {dir.name}
                      </span>
                    </button>
                  ))
                )}
              </div>
            </div>

            {/* Available Drives & Host Mount Points */}
            <div style={{ marginBottom: "1rem" }}>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 600,
                  marginBottom: "0.4rem",
                }}
              >
                Detected Host Mounts & Storage Drives:
              </div>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fill, minmax(220px, 1fr))",
                  gap: "0.5rem",
                }}
              >
                {(diskSpace ?? []).map((d, i) => (
                  <button
                    key={i}
                    type="button"
                    onClick={() => setBrowsingPath(d.path)}
                    className="btn btn-outline"
                    style={{
                      textAlign: "left",
                      padding: "0.5rem 0.75rem",
                      fontSize: "0.8rem",
                      display: "flex",
                      flexDirection: "column",
                      gap: "0.2rem",
                      borderColor:
                        browsingPath === d.path
                          ? "var(--primary, #3b82f6)"
                          : undefined,
                    }}
                  >
                    <div style={{ fontWeight: 600 }}>
                      💾 {d.label || "Drive"} ({d.path})
                    </div>
                    <div
                      style={{
                        fontSize: "0.7rem",
                        color: "var(--text-muted)",
                      }}
                    >
                      {formatBytes(d.freeSpace)} free / {formatBytes(d.totalSpace)}
                    </div>
                  </button>
                ))}
              </div>
            </div>

            {/* Quick Category Directory Suggestions */}
            <div style={{ marginBottom: "1.25rem" }}>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 600,
                  marginBottom: "0.4rem",
                }}
              >
                Common Category Paths:
              </div>
              <div style={{ display: "flex", flexWrap: "wrap", gap: "0.4rem" }}>
                {[
                  "/downloads/movies",
                  "/downloads/tv",
                  "/downloads/music",
                  "/downloads/anime",
                  "/downloads/books",
                  "/downloads/software",
                  "/data/torrents",
                ].map((preset) => (
                  <button
                    key={preset}
                    type="button"
                    onClick={() => setBrowsingPath(preset)}
                    className="btn btn-outline btn-small"
                    style={{
                      fontSize: "0.75rem",
                      padding: "0.25rem 0.5rem",
                      borderColor:
                        browsingPath === preset
                          ? "var(--primary, #3b82f6)"
                          : undefined,
                    }}
                  >
                    📂 {preset}
                  </button>
                ))}
              </div>
            </div>

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setShowFolderBrowser(false)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={() => handleSelectBrowserPath(browsingPath)}
              >
                Select This Folder
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export const CategorySettings = CategorySettingsTab;
export default CategorySettingsTab;
