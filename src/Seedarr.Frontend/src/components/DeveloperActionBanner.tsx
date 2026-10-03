export interface ActionMessage {
  type: "success" | "error";
  text: string;
}

export function DeveloperActionBanner({
  message,
}: {
  message?: ActionMessage | null;
}) {
  if (!message) return null;

  const isSuccess = message.type === "success";
  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        padding: "10px 14px",
        borderRadius: "6px",
        marginBottom: "16px",
        fontSize: "0.85rem",
        backgroundColor: isSuccess
          ? "rgba(16, 185, 129, 0.15)"
          : "rgba(239, 68, 68, 0.15)",
        border: `1px solid ${isSuccess ? "#10b981" : "#ef4444"}`,
        color: isSuccess ? "#34d399" : "#f87171",
      }}
    >
      {message.text}
    </div>
  );
}

export default DeveloperActionBanner;
