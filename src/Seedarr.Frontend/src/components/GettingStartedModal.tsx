import React, { useState, useEffect, useCallback, useMemo } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "../i18n";
import {
  useCreateDownloadClient,
  useTestDirectDownloadClient,
  useCreateIndexer,
  useTestDirectIndexer,
  useCreateArrConnection,
  useTestDirectArrConnection,
} from "../api/hooks";
import type {
  DownloadClientDefinition,
  DownloadClientTestResult,
  IndexerDefinition,
  IndexerTestResult,
  ArrConnection,
  ArrTestResult,
} from "../api/types";
import {
  TextInput,
  SelectInput,
  Toggle,
  NumberInput,
} from "../pages/settings/shared";
import SeedarrLogo from "./icons/SeedarrLogo";
import SeedarrText from "./icons/SeedarrText";
import { LanguageSelector } from "./LanguageSelector";

export const STORAGE_KEY_HIDE_GUIDE = "seedarr_hide_getting_started";

interface GettingStartedModalProps {
  isOpen: boolean;
  onClose: () => void;
}

type GuideMode = "readonly" | "interactive";

interface StepMeta {
  id: string;
  stepNum: number;
  shortName: string;
  title: string;
}

export function GettingStartedModal({
  isOpen,
  onClose,
}: GettingStartedModalProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [currentStep, setCurrentStep] = useState(0);
  const [mode, setMode] = useState<GuideMode>("readonly");
  const [dontShowAgain, setDontShowAgain] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) === "true";
  });

  const steps: StepMeta[] = useMemo(
    () => [
      {
        id: "welcome",
        stepNum: 0,
        shortName: t("gettingStarted.stepWelcome", undefined, "Welcome"),
        title: t(
          "gettingStarted.stepWelcomeTitle",
          undefined,
          "Welcome to Seedarr",
        ),
      },
      {
        id: "client",
        stepNum: 1,
        shortName: t(
          "gettingStarted.stepClient",
          undefined,
          "Download Client",
        ),
        title: t(
          "gettingStarted.stepClientTitle",
          undefined,
          "Add Download Client",
        ),
      },
      {
        id: "prowlarr",
        stepNum: 2,
        shortName: t("gettingStarted.stepProwlarr", undefined, "Prowlarr"),
        title: t(
          "gettingStarted.stepProwlarrTitle",
          undefined,
          "Add Indexer",
        ),
      },
      {
        id: "sonarr",
        stepNum: 3,
        shortName: t("gettingStarted.stepSonarr", undefined, "Sonarr"),
        title: t(
          "gettingStarted.stepSonarrTitle",
          undefined,
          "Add Connection",
        ),
      },
      {
        id: "radarr",
        stepNum: 4,
        shortName: t("gettingStarted.stepRadarr", undefined, "Radarr"),
        title: t(
          "gettingStarted.stepRadarrTitle",
          undefined,
          "Add Connection",
        ),
      },
      {
        id: "lidarr",
        stepNum: 5,
        shortName: t("gettingStarted.stepLidarr", undefined, "Lidarr"),
        title: t(
          "gettingStarted.stepLidarrTitle",
          undefined,
          "Add Connection",
        ),
      },
      {
        id: "finish",
        stepNum: 6,
        shortName: t("gettingStarted.stepFinished", undefined, "Finished"),
        title: t(
          "gettingStarted.stepFinishedTitle",
          undefined,
          "Setup Complete",
        ),
      },
    ],
    [t],
  );

  // Download Client Form State
  const [clientForm, setClientForm] = useState<
    Partial<DownloadClientDefinition>
  >({
    name: "My qBittorrent",
    clientType: "QBitTorrent",
    host: "localhost",
    port: 8080,
    useSsl: false,
    username: "",
    password: "",
    category: "",
    enable: true,
  });
  const [clientTestResult, setClientTestResult] =
    useState<DownloadClientTestResult | null>(null);
  const [clientSaved, setClientSaved] = useState(false);

  // Prowlarr Indexer Form State
  const [indexerForm, setIndexerForm] = useState<Partial<IndexerDefinition>>({
    name: "Prowlarr",
    indexerType: "Prowlarr",
    url: "http://prowlarr:9696",
    apiKey: "",
    apiPath: "/api",
    categories: "2000,5000",
    enable: true,
    enableRss: true,
    enableSearch: true,
  });
  const [indexerTestResult, setIndexerTestResult] =
    useState<IndexerTestResult | null>(null);
  const [indexerSaved, setIndexerSaved] = useState(false);

  // Sonarr Form State
  const [sonarrForm, setSonarrForm] = useState<Partial<ArrConnection>>({
    name: "Sonarr",
    arrType: "Sonarr",
    url: "http://localhost:8989",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    webhookHost: "seedarr",
  });
  const [sonarrTestResult, setSonarrTestResult] =
    useState<ArrTestResult | null>(null);
  const [sonarrSaved, setSonarrSaved] = useState(false);

  // Radarr Form State
  const [radarrForm, setRadarrForm] = useState<Partial<ArrConnection>>({
    name: "Radarr",
    arrType: "Radarr",
    url: "http://localhost:7878",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    webhookHost: "seedarr",
  });
  const [radarrTestResult, setRadarrTestResult] =
    useState<ArrTestResult | null>(null);
  const [radarrSaved, setRadarrSaved] = useState(false);

  // Lidarr Form State
  const [lidarrForm, setLidarrForm] = useState<Partial<ArrConnection>>({
    name: "Lidarr",
    arrType: "Lidarr",
    url: "http://localhost:8686",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    webhookHost: "seedarr",
  });
  const [lidarrTestResult, setLidarrTestResult] =
    useState<ArrTestResult | null>(null);
  const [lidarrSaved, setLidarrSaved] = useState(false);

  // API Mutations
  const testClientMutation = useTestDirectDownloadClient();
  const createClientMutation = useCreateDownloadClient();

  const testIndexerMutation = useTestDirectIndexer();
  const createIndexerMutation = useCreateIndexer();

  const testArrMutation = useTestDirectArrConnection();
  const createArrMutation = useCreateArrConnection();

  const handleClose = useCallback(() => {
    if (dontShowAgain) {
      localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "true");
    }
    onClose();
  }, [dontShowAgain, onClose]);

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        handleClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, handleClose]);

  if (!isOpen) return null;

  const handleDontShowChange = (checked: boolean) => {
    setDontShowAgain(checked);
    if (checked) {
      localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "true");
    } else {
      localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "false");
    }
  };

  const handleNext = () => {
    if (currentStep < steps.length - 1) {
      setCurrentStep((p) => p + 1);
    } else {
      handleClose();
    }
  };

  const handlePrev = () => {
    if (currentStep > 0) {
      setCurrentStep((p) => p - 1);
    }
  };

  const isReadOnly = mode === "readonly";

  // Client Defaults helper
  const clientDefaults: Record<string, { port: number }> = {
    QBitTorrent: { port: 8080 },
    Transmission: { port: 9091 },
    Deluge: { port: 8112 },
  };

  // Test Connection Handlers
  const handleTestClient = () => {
    setClientTestResult(null);
    testClientMutation.mutate(clientForm, {
      onSuccess: (data) => setClientTestResult(data),
      onError: (err) =>
        setClientTestResult({ success: false, message: err.message }),
    });
  };

  const handleSaveClient = () => {
    createClientMutation.mutate(
      {
        ...clientForm,
        name:
          clientForm.name?.trim() ||
          clientForm.clientType ||
          t("gettingStarted.stepClient", undefined, "Download Client"),
        implementation: `${clientForm.clientType || "QBitTorrent"}DownloadClient`,
        configContract: "DownloadClientDefinition",
      },
      {
        onSuccess: () => {
          setClientSaved(true);
          handleNext();
        },
      },
    );
  };

  const handleTestIndexer = () => {
    setIndexerTestResult(null);
    testIndexerMutation.mutate(indexerForm, {
      onSuccess: (data) => setIndexerTestResult(data),
      onError: (err) =>
        setIndexerTestResult({ success: false, message: err.message }),
    });
  };

  const handleSaveIndexer = () => {
    createIndexerMutation.mutate(
      {
        ...indexerForm,
        name: indexerForm.name?.trim() || "Prowlarr",
        implementation: `${indexerForm.indexerType || "Prowlarr"}Indexer`,
        configContract: "IndexerDefinition",
      },
      {
        onSuccess: () => {
          setIndexerSaved(true);
          handleNext();
        },
      },
    );
  };

  const handleTestArr = (
    form: Partial<ArrConnection>,
    setResult: (res: ArrTestResult | null) => void,
  ) => {
    setResult(null);
    testArrMutation.mutate(form, {
      onSuccess: (data) => setResult(data),
      onError: (err) => setResult({ success: false, message: err.message }),
    });
  };

  const handleSaveArr = (
    form: Partial<ArrConnection>,
    setSaved: (saved: boolean) => void,
    arrType: string,
  ) => {
    createArrMutation.mutate(
      {
        ...form,
        arrType,
        name: form.name?.trim() || arrType,
        implementation: `${arrType}Connection`,
        configContract: "ArrConnectionDefinition",
      },
      {
        onSuccess: () => {
          setSaved(true);
          handleNext();
        },
      },
    );
  };

  // Helper for rendering connection test feedback alert
  const renderTestAlert = (
    isPending: boolean,
    result: { success: boolean; message?: string } | null,
    targetName: string,
  ) => {
    if (isPending) {
      return (
        <div
          style={{
            marginTop: "1rem",
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            fontSize: "0.875rem",
            backgroundColor: "rgba(200, 168, 78, 0.12)",
            color: "var(--accent, #c8a84e)",
            border: "1px solid rgba(200, 168, 78, 0.35)",
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
          }}
        >
          <span>
            {t(
              "gettingStarted.testingTo",
              { target: targetName },
              `Testing connection to ${targetName}...`,
            )}
          </span>
        </div>
      );
    }

    if (result) {
      return (
        <div
          style={{
            marginTop: "1rem",
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            fontSize: "0.875rem",
            lineHeight: "1.4",
            display: "flex",
            alignItems: "flex-start",
            gap: "0.65rem",
            backgroundColor: result.success
              ? "rgba(40, 167, 69, 0.15)"
              : "rgba(220, 53, 69, 0.15)",
            color: result.success
              ? "var(--success, #28a745)"
              : "var(--danger, #dc3545)",
            border: `1px solid ${
              result.success
                ? "rgba(40, 167, 69, 0.35)"
                : "rgba(220, 53, 69, 0.35)"
            }`,
          }}
        >
          <span
            style={{ fontWeight: "bold", fontSize: "1.1rem", lineHeight: "1" }}
          >
            {result.success ? "✓" : "✕"}
          </span>
          <div style={{ flex: 1 }}>
            <div style={{ fontWeight: 600 }}>
              {result.success
                ? t(
                    "gettingStarted.connectionSuccess",
                    undefined,
                    "Connection Successful",
                  )
                : t(
                    "gettingStarted.connectionFailed",
                    undefined,
                    "Connection Failed",
                  )}
            </div>
            {result.message && (
              <div
                style={{
                  marginTop: "0.25rem",
                  opacity: 0.95,
                  wordBreak: "break-word",
                }}
              >
                {result.message}
              </div>
            )}
          </div>
        </div>
      );
    }

    return null;
  };

  return (
    <div
      className="modal-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="getting-started-modal-title"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(10, 11, 18, 0.85)",
        backdropFilter: "blur(6px)",
        WebkitBackdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1rem",
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          maxWidth: 540,
          width: "92vw",
          maxHeight: "90vh",
          overflowY: "auto",
          backgroundColor: "var(--bg-secondary, #2a2620)",
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0, 0, 0, 0.7)",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.15))",
          padding: "1.5rem",
          display: "flex",
          flexDirection: "column",
        }}
      >
        {/* Top Header Controls: Mode Selector & Close Button */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
          }}
        >
          {/* Mode Selector */}
          <div
            style={{
              display: "inline-flex",
              background: "var(--bg-primary, #18181f)",
              padding: "2px",
              borderRadius: "20px",
              border: "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
              fontSize: "0.75rem",
            }}
          >
            <button
              type="button"
              onClick={() => setMode("readonly")}
              style={{
                background:
                  mode === "readonly"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color: mode === "readonly" ? "#000" : "var(--text-muted, #aaa)",
                border: "none",
                padding: "3px 10px",
                borderRadius: "16px",
                fontWeight: mode === "readonly" ? 600 : 400,
                cursor: "pointer",
              }}
              title={t(
                "gettingStarted.guideMode",
                undefined,
                "Tour mode with example preview",
              )}
            >
              👁️ {t("gettingStarted.guideMode", undefined, "Tour / Example")}
            </button>
            <button
              type="button"
              onClick={() => setMode("interactive")}
              style={{
                background:
                  mode === "interactive"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color:
                  mode === "interactive" ? "#000" : "var(--text-muted, #aaa)",
                border: "none",
                padding: "3px 10px",
                borderRadius: "16px",
                fontWeight: mode === "interactive" ? 600 : 400,
                cursor: "pointer",
              }}
              title={t(
                "gettingStarted.liveSetupMode",
                undefined,
                "Live setup to test and save credentials",
              )}
            >
              ⚡ {t("gettingStarted.liveSetupMode", undefined, "Live Setup")}
            </button>
          </div>

          {/* Right Controls: Language Selector & Close Button */}
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <LanguageSelector />
            <button
              type="button"
              className="btn btn-outline"
              style={{
                padding: "0.2rem 0.5rem",
                fontSize: "0.85rem",
                borderRadius: "4px",
                lineHeight: 1,
              }}
              onClick={handleClose}
              title={t("gettingStarted.close", undefined, "Close Setup Guide (Esc)")}
            >
              ✕
            </button>
          </div>
        </div>

        {/* Step Indicator Breadcrumbs */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: "1.25rem",
            paddingBottom: "0.75rem",
            borderBottom:
              "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            gap: "0.25rem",
            overflowX: "auto",
          }}
        >
          {steps.map((s, idx) => {
            const isActive = idx === currentStep;
            const isCompleted = idx < currentStep;
            return (
              <button
                key={s.id}
                type="button"
                onClick={() => setCurrentStep(idx)}
                style={{
                  background: isActive
                    ? "var(--accent, #c8a84e)"
                    : isCompleted
                      ? "rgba(255, 255, 255, 0.08)"
                      : "transparent",
                  color: isActive
                    ? "#000"
                    : isCompleted
                      ? "var(--text-primary)"
                      : "var(--text-muted)",
                  border: "none",
                  borderRadius: "12px",
                  padding: "2px 8px",
                  fontSize: "0.72rem",
                  fontWeight: isActive ? 600 : 400,
                  cursor: "pointer",
                  whiteSpace: "nowrap",
                }}
              >
                {s.shortName}
              </button>
            );
          })}
        </div>

        {/* Modal Title matching actual modals */}
        <div
          className="modal-title"
          style={{
            fontSize: "1.2rem",
            marginBottom: "1.25rem",
            color: "var(--text-primary)",
            fontWeight: 600,
          }}
        >
          {steps[currentStep]?.title}
        </div>

        {/* ========================================================================= */}
        {/* STEP 0: Welcome */}
        {/* ========================================================================= */}
        {currentStep === 0 && (
          <div style={{ textAlign: "center", padding: "1rem 0.5rem" }}>
            <div style={{ marginBottom: "0.75rem" }}>
              <SeedarrLogo size={72} />
            </div>
            <div style={{ marginBottom: "1.25rem" }}>
              <SeedarrText width={140} />
            </div>
            <p
              style={{
                color: "var(--text-muted)",
                fontSize: "0.9rem",
                lineHeight: 1.5,
                margin: "0 0 1.5rem",
              }}
            >
              {t(
                "gettingStarted.welcomeDescription",
                undefined,
                "Seedarr connects to your Download Agent (qBittorrent, Transmission, Deluge), Prowlarr Indexer, and *Arr Media Managers (Sonarr, Radarr, Lidarr) for automated cross-seeding and swarm optimization.",
              )}
            </p>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
                textAlign: "left",
                backgroundColor: "var(--bg-secondary)",
                padding: "1rem",
                borderRadius: "6px",
                border:
                  "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
                marginBottom: "1.5rem",
                fontSize: "0.85rem",
              }}
            >
              <div>
                <strong>
                  {t(
                    "gettingStarted.welcomeAgent",
                    undefined,
                    "1. Download Agent: Captures downloads & monitors torrent swarms.",
                  )}
                </strong>
              </div>
              <div>
                <strong>
                  {t(
                    "gettingStarted.welcomeProwlarr",
                    undefined,
                    "2. Prowlarr: Syncs indexers and trackers automatically.",
                  )}
                </strong>
              </div>
              <div>
                <strong>
                  {t(
                    "gettingStarted.welcomeArr",
                    undefined,
                    "3. Sonarr / Radarr / Lidarr: Connects TV, movies, and music libraries.",
                  )}
                </strong>
              </div>
            </div>

            {/* Language Choice Selection Row */}
            <div
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.5rem",
                marginBottom: "1.5rem",
                padding: "0.3rem 0.75rem",
                backgroundColor: "var(--bg-secondary, rgba(0, 0, 0, 0.2))",
                borderRadius: "6px",
                border:
                  "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              }}
            >
              <span
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted, #aaa)",
                }}
              >
                🌐 {t("gettingStarted.language", undefined, "Language:")}
              </span>
              <LanguageSelector />
            </div>

            <div
              style={{
                display: "flex",
                justifyContent: "center",
                gap: "0.75rem",
              }}
            >
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={() => {
                  setMode("readonly");
                  setCurrentStep(1);
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                {t(
                  "gettingStarted.startExampleTour",
                  undefined,
                  "Start Example Tour →",
                )}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => {
                  setMode("interactive");
                  setCurrentStep(1);
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                ⚡{" "}
                {t(
                  "gettingStarted.startLiveSetup",
                  undefined,
                  "Start Live Setup",
                )}
              </button>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 1: Download Client Form */}
        {/* ========================================================================= */}
        {currentStep === 1 && (
          <div>
            <TextInput
              label={t("gettingStarted.name", undefined, "Name")}
              value={clientForm.name || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, name: v });
              }}
              placeholder="My qBittorrent"
              disabled={isReadOnly}
            />
            <SelectInput
              label={t("gettingStarted.clientType", undefined, "Client Type")}
              value={clientForm.clientType || "QBitTorrent"}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({
                  ...clientForm,
                  clientType: v,
                  port: clientDefaults[v]?.port || clientForm.port || 8080,
                });
              }}
              options={[
                { value: "QBitTorrent", label: "qBittorrent" },
                { value: "Transmission", label: "Transmission" },
                { value: "Deluge", label: "Deluge" },
              ]}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.host", undefined, "Host")}
              value={clientForm.host || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, host: v });
              }}
              placeholder="localhost"
              disabled={isReadOnly}
            />
            <NumberInput
              label={t("gettingStarted.port", undefined, "Port")}
              value={clientForm.port || 8080}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, port: v });
              }}
              min={1}
              max={65535}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.useSsl", undefined, "Use SSL")}
              checked={clientForm.useSsl ?? false}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, useSsl: v });
              }}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.username", undefined, "Username")}
              value={clientForm.username || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, username: v });
              }}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.password", undefined, "Password")}
              value={isReadOnly ? "••••••••••••" : clientForm.password || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, password: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.category", undefined, "Category")}
              value={clientForm.category || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, category: v });
              }}
              hint={t(
                "gettingStarted.categoryHint",
                undefined,
                "Filter by category",
              )}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.enabled", undefined, "Enabled")}
              checked={clientForm.enable ?? true}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, enable: v });
              }}
              disabled={isReadOnly}
            />

            {renderTestAlert(
              testClientMutation.isPending,
              clientTestResult,
              clientForm.host || "client",
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={handleTestClient}
                disabled={testClientMutation.isPending || isReadOnly}
              >
                {testClientMutation.isPending
                  ? t("gettingStarted.testing", undefined, "Testing...")
                  : t(
                      "gettingStarted.testConnection",
                      undefined,
                      "Test Connection",
                    )}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  {t("gettingStarted.previous", undefined, "Previous")}
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleSaveClient}
                    disabled={createClientMutation.isPending}
                  >
                    {createClientMutation.isPending
                      ? t("gettingStarted.saving", undefined, "Saving...")
                      : clientSaved
                        ? t(
                            "gettingStarted.savedNext",
                            undefined,
                            "Saved ✓ Next",
                          )
                        : t(
                            "gettingStarted.saveAndNext",
                            undefined,
                            "Save & Next",
                          )}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    {t("gettingStarted.next", undefined, "Next")}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 2: Prowlarr Indexer Form */}
        {/* ========================================================================= */}
        {currentStep === 2 && (
          <div>
            <TextInput
              label={t("gettingStarted.name", undefined, "Name")}
              value={indexerForm.name || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, name: v });
              }}
              placeholder="Prowlarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label={t("gettingStarted.type", undefined, "Type")}
              value={indexerForm.indexerType || "Prowlarr"}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({
                  ...indexerForm,
                  indexerType: v,
                });
              }}
              options={[
                { value: "Prowlarr", label: "Prowlarr" },
                { value: "Torznab", label: "Torznab" },
                { value: "Newznab", label: "Newznab" },
              ]}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.url", undefined, "URL")}
              value={indexerForm.url || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, url: v });
              }}
              placeholder="http://prowlarr:9696"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.apiKey", undefined, "API Key")}
              value={
                isReadOnly
                  ? "••••••••••••••••••••••••••••••••"
                  : indexerForm.apiKey || ""
              }
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.apiPath", undefined, "API Path")}
              value={indexerForm.apiPath || "/api"}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, apiPath: v });
              }}
              placeholder="/api"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.categories", undefined, "Categories")}
              value={indexerForm.categories || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, categories: v });
              }}
              placeholder="2000,5000"
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.enable", undefined, "Enable")}
              checked={indexerForm.enable ?? true}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.enableRss", undefined, "RSS")}
              checked={indexerForm.enableRss ?? true}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, enableRss: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.enableSearch", undefined, "Search")}
              checked={indexerForm.enableSearch ?? true}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, enableSearch: v });
              }}
              disabled={isReadOnly}
            />

            {renderTestAlert(
              testIndexerMutation.isPending,
              indexerTestResult,
              indexerForm.url || "Prowlarr",
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={handleTestIndexer}
                disabled={testIndexerMutation.isPending || isReadOnly}
              >
                {testIndexerMutation.isPending
                  ? t("gettingStarted.testing", undefined, "Testing...")
                  : t(
                      "gettingStarted.testConnection",
                      undefined,
                      "Test Connection",
                    )}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  {t("gettingStarted.previous", undefined, "Previous")}
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleSaveIndexer}
                    disabled={createIndexerMutation.isPending}
                  >
                    {createIndexerMutation.isPending
                      ? t("gettingStarted.saving", undefined, "Saving...")
                      : indexerSaved
                        ? t(
                            "gettingStarted.savedNext",
                            undefined,
                            "Saved ✓ Next",
                          )
                        : t(
                            "gettingStarted.saveAndNext",
                            undefined,
                            "Save & Next",
                          )}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    {t("gettingStarted.next", undefined, "Next")}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 3: Sonarr Connection Form */}
        {/* ========================================================================= */}
        {currentStep === 3 && (
          <div>
            <TextInput
              label={t("gettingStarted.name", undefined, "Name")}
              value={sonarrForm.name || ""}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, name: v });
              }}
              placeholder="Sonarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label={t("gettingStarted.type", undefined, "Type")}
              value={sonarrForm.arrType || "Sonarr"}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, arrType: v });
              }}
              options={[
                { value: "Sonarr", label: "Sonarr" },
                { value: "Radarr", label: "Radarr" },
                { value: "Lidarr", label: "Lidarr" },
              ]}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.url", undefined, "URL")}
              value={sonarrForm.url || ""}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, url: v });
              }}
              placeholder="http://localhost:8989"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.apiKey", undefined, "API Key")}
              value={
                isReadOnly
                  ? "••••••••••••••••••••••••••••••••"
                  : sonarrForm.apiKey || ""
              }
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <Toggle
              label={t(
                "gettingStarted.enableConnection",
                undefined,
                "Enable Connection",
              )}
              checked={sonarrForm.enable ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t(
                "gettingStarted.syncEnabled",
                undefined,
                "Sync Enabled",
              )}
              checked={sonarrForm.syncEnabled ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, syncEnabled: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.autoAdd", undefined, "Auto Add")}
              checked={sonarrForm.enableAutomaticAdd ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, enableAutomaticAdd: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.webhook", undefined, "Webhook")}
              checked={sonarrForm.webhookEnabled ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, webhookEnabled: v });
              }}
              disabled={isReadOnly}
            />
            {sonarrForm.webhookEnabled !== false && (
              <TextInput
                label={t(
                  "gettingStarted.webhookHost",
                  undefined,
                  "Webhook Host",
                )}
                value={sonarrForm.webhookHost || ""}
                onChange={(v) => {
                  setSonarrTestResult(null);
                  setSonarrForm({ ...sonarrForm, webhookHost: v });
                }}
                placeholder="seedarr"
                hint={t(
                  "gettingStarted.webhookHostHint",
                  undefined,
                  "Hostname or IP for *arr to reach Seedarr (leave empty to use default)",
                )}
                disabled={isReadOnly}
              />
            )}

            {renderTestAlert(
              testArrMutation.isPending,
              sonarrTestResult,
              sonarrForm.url || "Sonarr",
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => handleTestArr(sonarrForm, setSonarrTestResult)}
                disabled={testArrMutation.isPending || isReadOnly}
              >
                {testArrMutation.isPending
                  ? t("gettingStarted.testing", undefined, "Testing...")
                  : t(
                      "gettingStarted.testConnection",
                      undefined,
                      "Test Connection",
                    )}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  {t("gettingStarted.previous", undefined, "Previous")}
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={() =>
                      handleSaveArr(sonarrForm, setSonarrSaved, "Sonarr")
                    }
                    disabled={createArrMutation.isPending}
                  >
                    {createArrMutation.isPending
                      ? t("gettingStarted.saving", undefined, "Saving...")
                      : sonarrSaved
                        ? t(
                            "gettingStarted.savedNext",
                            undefined,
                            "Saved ✓ Next",
                          )
                        : t(
                            "gettingStarted.saveAndNext",
                            undefined,
                            "Save & Next",
                          )}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    {t("gettingStarted.next", undefined, "Next")}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 4: Radarr Connection Form */}
        {/* ========================================================================= */}
        {currentStep === 4 && (
          <div>
            <TextInput
              label={t("gettingStarted.name", undefined, "Name")}
              value={radarrForm.name || ""}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, name: v });
              }}
              placeholder="Radarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label={t("gettingStarted.type", undefined, "Type")}
              value={radarrForm.arrType || "Radarr"}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, arrType: v });
              }}
              options={[
                { value: "Sonarr", label: "Sonarr" },
                { value: "Radarr", label: "Radarr" },
                { value: "Lidarr", label: "Lidarr" },
              ]}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.url", undefined, "URL")}
              value={radarrForm.url || ""}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, url: v });
              }}
              placeholder="http://localhost:7878"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.apiKey", undefined, "API Key")}
              value={
                isReadOnly
                  ? "••••••••••••••••••••••••••••••••"
                  : radarrForm.apiKey || ""
              }
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <Toggle
              label={t(
                "gettingStarted.enableConnection",
                undefined,
                "Enable Connection",
              )}
              checked={radarrForm.enable ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t(
                "gettingStarted.syncEnabled",
                undefined,
                "Sync Enabled",
              )}
              checked={radarrForm.syncEnabled ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, syncEnabled: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.autoAdd", undefined, "Auto Add")}
              checked={radarrForm.enableAutomaticAdd ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, enableAutomaticAdd: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.webhook", undefined, "Webhook")}
              checked={radarrForm.webhookEnabled ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, webhookEnabled: v });
              }}
              disabled={isReadOnly}
            />
            {radarrForm.webhookEnabled !== false && (
              <TextInput
                label={t(
                  "gettingStarted.webhookHost",
                  undefined,
                  "Webhook Host",
                )}
                value={radarrForm.webhookHost || ""}
                onChange={(v) => {
                  setRadarrTestResult(null);
                  setRadarrForm({ ...radarrForm, webhookHost: v });
                }}
                placeholder="seedarr"
                hint={t(
                  "gettingStarted.webhookHostHint",
                  undefined,
                  "Hostname or IP for *arr to reach Seedarr (leave empty to use default)",
                )}
                disabled={isReadOnly}
              />
            )}

            {renderTestAlert(
              testArrMutation.isPending,
              radarrTestResult,
              radarrForm.url || "Radarr",
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => handleTestArr(radarrForm, setRadarrTestResult)}
                disabled={testArrMutation.isPending || isReadOnly}
              >
                {testArrMutation.isPending
                  ? t("gettingStarted.testing", undefined, "Testing...")
                  : t(
                      "gettingStarted.testConnection",
                      undefined,
                      "Test Connection",
                    )}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  {t("gettingStarted.previous", undefined, "Previous")}
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={() =>
                      handleSaveArr(radarrForm, setRadarrSaved, "Radarr")
                    }
                    disabled={createArrMutation.isPending}
                  >
                    {createArrMutation.isPending
                      ? t("gettingStarted.saving", undefined, "Saving...")
                      : radarrSaved
                        ? t(
                            "gettingStarted.savedNext",
                            undefined,
                            "Saved ✓ Next",
                          )
                        : t(
                            "gettingStarted.saveAndNext",
                            undefined,
                            "Save & Next",
                          )}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    {t("gettingStarted.next", undefined, "Next")}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 5: Lidarr Connection Form */}
        {/* ========================================================================= */}
        {currentStep === 5 && (
          <div>
            <TextInput
              label={t("gettingStarted.name", undefined, "Name")}
              value={lidarrForm.name || ""}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, name: v });
              }}
              placeholder="Lidarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label={t("gettingStarted.type", undefined, "Type")}
              value={lidarrForm.arrType || "Lidarr"}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, arrType: v });
              }}
              options={[
                { value: "Sonarr", label: "Sonarr" },
                { value: "Radarr", label: "Radarr" },
                { value: "Lidarr", label: "Lidarr" },
              ]}
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.url", undefined, "URL")}
              value={lidarrForm.url || ""}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, url: v });
              }}
              placeholder="http://localhost:8686"
              disabled={isReadOnly}
            />
            <TextInput
              label={t("gettingStarted.apiKey", undefined, "API Key")}
              value={
                isReadOnly
                  ? "••••••••••••••••••••••••••••••••"
                  : lidarrForm.apiKey || ""
              }
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <Toggle
              label={t(
                "gettingStarted.enableConnection",
                undefined,
                "Enable Connection",
              )}
              checked={lidarrForm.enable ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t(
                "gettingStarted.syncEnabled",
                undefined,
                "Sync Enabled",
              )}
              checked={lidarrForm.syncEnabled ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, syncEnabled: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.autoAdd", undefined, "Auto Add")}
              checked={lidarrForm.enableAutomaticAdd ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, enableAutomaticAdd: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label={t("gettingStarted.webhook", undefined, "Webhook")}
              checked={lidarrForm.webhookEnabled ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, webhookEnabled: v });
              }}
              disabled={isReadOnly}
            />
            {lidarrForm.webhookEnabled !== false && (
              <TextInput
                label={t(
                  "gettingStarted.webhookHost",
                  undefined,
                  "Webhook Host",
                )}
                value={lidarrForm.webhookHost || ""}
                onChange={(v) => {
                  setLidarrTestResult(null);
                  setLidarrForm({ ...lidarrForm, webhookHost: v });
                }}
                placeholder="seedarr"
                hint={t(
                  "gettingStarted.webhookHostHint",
                  undefined,
                  "Hostname or IP for *arr to reach Seedarr (leave empty to use default)",
                )}
                disabled={isReadOnly}
              />
            )}

            {renderTestAlert(
              testArrMutation.isPending,
              lidarrTestResult,
              lidarrForm.url || "Lidarr",
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => handleTestArr(lidarrForm, setLidarrTestResult)}
                disabled={testArrMutation.isPending || isReadOnly}
              >
                {testArrMutation.isPending
                  ? t("gettingStarted.testing", undefined, "Testing...")
                  : t(
                      "gettingStarted.testConnection",
                      undefined,
                      "Test Connection",
                    )}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  {t("gettingStarted.previous", undefined, "Previous")}
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={() =>
                      handleSaveArr(lidarrForm, setLidarrSaved, "Lidarr")
                    }
                    disabled={createArrMutation.isPending}
                  >
                    {createArrMutation.isPending
                      ? t("gettingStarted.saving", undefined, "Saving...")
                      : lidarrSaved
                        ? t(
                            "gettingStarted.savedNext",
                            undefined,
                            "Saved ✓ Next",
                          )
                        : t(
                            "gettingStarted.saveAndNext",
                            undefined,
                            "Save & Next",
                          )}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    {t("gettingStarted.next", undefined, "Next")}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 6: Finished */}
        {/* ========================================================================= */}
        {currentStep === 6 && (
          <div style={{ textAlign: "center", padding: "1rem 0.5rem" }}>
            <div style={{ fontSize: "3rem", marginBottom: "0.5rem" }}>🎉</div>
            <p
              style={{
                color: "var(--text-muted)",
                fontSize: "0.9rem",
                lineHeight: 1.5,
                margin: "0 0 1.5rem",
              }}
            >
              {t(
                "gettingStarted.finishDescription",
                undefined,
                "Your connections are set! Seedarr is ready to harvest swarm trackers, coordinate seeding, and sync with your media library.",
              )}
            </p>

            <div
              style={{
                display: "flex",
                justifyContent: "center",
                gap: "0.75rem",
                flexWrap: "wrap",
              }}
            >
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={() => {
                  handleClose();
                  navigate("/");
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                📊{" "}
                {t(
                  "gettingStarted.goToDashboard",
                  undefined,
                  "Go to Dashboard",
                )}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => {
                  handleClose();
                  navigate("/torrents");
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                📦{" "}
                {t(
                  "gettingStarted.viewTorrents",
                  undefined,
                  "View Torrents",
                )}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => {
                  handleClose();
                  navigate("/settings/general");
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                ⚙️ {t("gettingStarted.settings", undefined, "Settings")}
              </button>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* Bottom Footer: "Don't show this guide on startup" & Step indicator */}
        {/* ========================================================================= */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginTop: "1.25rem",
            paddingTop: "0.75rem",
            borderTop:
              "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            fontSize: "0.8rem",
            color: "var(--text-muted)",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              cursor: "pointer",
              userSelect: "none",
            }}
          >
            <input
              type="checkbox"
              checked={dontShowAgain}
              onChange={(e) => handleDontShowChange(e.target.checked)}
              style={{
                cursor: "pointer",
                accentColor: "var(--accent, #c8a84e)",
              }}
            />
            <span>
              {t(
                "gettingStarted.dontShowAgain",
                undefined,
                "Don't show this guide on startup",
              )}
            </span>
          </label>

          <span>
            {t(
              "gettingStarted.stepCount",
              { current: currentStep + 1, total: steps.length },
              `Step ${currentStep + 1} of ${steps.length}`,
            )}
          </span>
        </div>
      </div>
    </div>
  );
}

export default GettingStartedModal;
