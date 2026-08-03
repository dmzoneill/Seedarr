import { SkeletonLine } from "../../components/Skeleton";

export function StatusRow({
  label,
  children,
  mono,
}: {
  label: string;
  children: React.ReactNode;
  mono?: boolean;
}) {
  return (
    <div className="status-row">
      <span className="status-label">{label}</span>
      <span className={`status-value${mono ? " mono" : ""}`}>{children}</span>
    </div>
  );
}

export function TorrentDetailSkeleton() {
  return (
    <>
      <SkeletonLine width="40%" height="1.5rem" />
      <div className="detail-grid" style={{ marginTop: "1.5rem" }}>
        <div className="card">
          <SkeletonLine width="30%" height="1rem" />
          {[0, 1, 2, 3, 4].map((i) => (
            <div key={i} className="status-row">
              <SkeletonLine width="25%" height="0.85rem" />
              <SkeletonLine width="40%" height="0.85rem" />
            </div>
          ))}
        </div>
        <div className="card">
          <SkeletonLine width="30%" height="1rem" />
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <div key={i} className="status-row">
              <SkeletonLine width="25%" height="0.85rem" />
              <SkeletonLine width="40%" height="0.85rem" />
            </div>
          ))}
        </div>
      </div>
    </>
  );
}
