import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router";
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
import { TextInput, SelectInput, Toggle, NumberInput } from "../pages/settings/shared";
import SeedarrLogo from "./icons/SeedarrLogo";
import SeedarrText from "./icons/SeedarrText";

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

const STEPS: StepMeta[] = [
  { id: "welcome", stepNum: 0, shortName: "Welcome", title: "Welcome to Seedarr" },
  { id: "client", stepNum: 1, shortName: "Download Client", title: "Add Download Client" },
  { id: "prowlarr", stepNum: 2, shortName: "Prowlarr", title: "Add Indexer" },
  { id: "sonarr", stepNum: 3, shortName: "Sonarr", title: "Add Connection" },
  { id: "radarr", stepNum: 4, shortName: "Radarr", title: "Add Connection" },
  { id: "lidarr", stepNum: 5, shortName: "Lidarr", title: "Add Connection" },
  { id: "finish", stepNum: 6, shortName: "Finished", title: "Setup Complete" },
];

export function GettingStartedModal({ isOpen, onClose }: GettingStartedModalProps) {
  const navigate = useNavigate();
  const [currentStep, setCurrentStep] = useState(0);
  const [mode, setMode] = useState<GuideMode>("readonly");
  const [dontShowAgain, setDontShowAgain] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) === "true";
  });

  // Download Client Form State
  const [clientForm, setClientForm] = useState<Partial<DownloadClientDefinition>>({
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
  const [clientTestResult, setClientTestResult] = useState<DownloadClientTestResult | null>(null);
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
  const [indexerTestResult, setIndexerTestResult] = useState<IndexerTestResult | null>(null);
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
  const [sonarrTestResult, setSonarrTestResult] = useState<ArrTestResult | null>(null);
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
  const [radarrTestResult, setRadarrTestResult] = useState<ArrTestResult | null>(null);
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
  const [lidarrTestResult, setLidarrTestResult] = useState<ArrTestResult | null>(null);
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
    if (currentStep < STEPS.length - 1) {
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
      onError: (err) => setClientTestResult({ success: false, message: err.message }),
    });
  };

  const handleSaveClient = () => {
    createClientMutation.mutate(
      {
        ...clientForm,
        name: clientForm.name?.trim() || clientForm.clientType || "Download Client",
        implementation: `${clientForm.clientType || "QBitTorrent"}DownloadClient`,
        configContract: "DownloadClientDefinition",
      },
      {
        onSuccess: () => {
          setClientSaved(true);
          handleNext();
        },
      }
    );
  };

  const handleTestIndexer = () => {
    setIndexerTestResult(null);
    testIndexerMutation.mutate(indexerForm, {
      onSuccess: (data) => setIndexerTestResult(data),
      onError: (err) => setIndexerTestResult({ success: false, message: err.message }),
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
      }
    );
  };

  const handleTestArr = (form: Partial<ArrConnection>, setResult: (res: ArrTestResult | null) => void) => {
    setResult(null);
    testArrMutation.mutate(form, {
      onSuccess: (data) => setResult(data),
      onError: (err) => setResult({ success: false, message: err.message }),
    });
  };

  const handleSaveArr = (
    form: Partial<ArrConnection>,
    setSaved: (saved: boolean) => void,
    arrType: string
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
      }
    );
  };

  // Helper for rendering connection test feedback alert
  const renderTestAlert = (
    isPending: boolean,
    result: { success: boolean; message?: string } | null,
    targetName: string
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
          <span>Testing connection to {targetName}...</span>
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
            color: result.success ? "var(--success, #28a745)" : "var(--danger, #dc3545)",
            border: `1px solid ${
              result.success ? "rgba(40, 167, 69, 0.35)" : "rgba(220, 53, 69, 0.35)"
            }`,
          }}
        >
          <span style={{ fontWeight: "bold", fontSize: "1.1rem", lineHeight: "1" }}>
            {result.success ? "✓" : "✕"}
          </span>
          <div style={{ flex: 1 }}>
            <div style={{ fontWeight: 600 }}>
              {result.success ? "Connection Successful" : "Connection Failed"}
            </div>
            {result.message && (
              <div style={{ marginTop: "0.25rem", opacity: 0.95, wordBreak: "break-word" }}>
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
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
          border: "1px solid rgba(255, 255, 255, 0.12)",
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
                background: mode === "readonly" ? "var(--accent, #c8a84e)" : "transparent",
                color: mode === "readonly" ? "#000" : "var(--text-muted, #aaa)",
                border: "none",
                padding: "3px 10px",
                borderRadius: "16px",
                fontWeight: mode === "readonly" ? 600 : 400,
                cursor: "pointer",
              }}
              title="Tour mode with example preview"
            >
              👁️ Tour / Example
            </button>
            <button
              type="button"
              onClick={() => setMode("interactive")}
              style={{
                background: mode === "interactive" ? "var(--accent, #c8a84e)" : "transparent",
                color: mode === "interactive" ? "#000" : "var(--text-muted, #aaa)",
                border: "none",
                padding: "3px 10px",
                borderRadius: "16px",
                fontWeight: mode === "interactive" ? 600 : 400,
                cursor: "pointer",
              }}
              title="Live setup to test and save credentials"
            >
              ⚡ Live Setup
            </button>
          </div>

          {/* Close Button */}
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
            title="Close Setup Guide (Esc)"
          >
            ✕
          </button>
        </div>

        {/* Step Indicator Breadcrumbs */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: "1.25rem",
            paddingBottom: "0.75rem",
            borderBottom: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            gap: "0.25rem",
            overflowX: "auto",
          }}
        >
          {STEPS.map((s, idx) => {
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
                  color: isActive ? "#000" : isCompleted ? "var(--text-primary)" : "var(--text-muted)",
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
          {STEPS[currentStep].title}
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
              Seedarr connects to your <strong>Download Agent</strong> (qBittorrent, Transmission, Deluge),
              <strong>Prowlarr Indexer</strong>, and <strong>*Arr Media Managers</strong> (Sonarr, Radarr, Lidarr)
              for automated cross-seeding and swarm optimization.
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
                border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
                marginBottom: "1.5rem",
                fontSize: "0.85rem",
              }}
            >
              <div><strong>1. Download Agent:</strong> Captures downloads & monitors torrent swarms.</div>
              <div><strong>2. Prowlarr:</strong> Syncs indexers and trackers automatically.</div>
              <div><strong>3. Sonarr / Radarr / Lidarr:</strong> Connects TV, movies, and music libraries.</div>
            </div>

            <div style={{ display: "flex", justifyContent: "center", gap: "0.75rem" }}>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={() => {
                  setMode("readonly");
                  setCurrentStep(1);
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                Start Example Tour →
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
                ⚡ Start Live Setup
              </button>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 1: Download Client Form (Matches Screenshot 3) */}
        {/* ========================================================================= */}
        {currentStep === 1 && (
          <div>
            <TextInput
              label="Name"
              value={clientForm.name || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, name: v });
              }}
              placeholder="My qBittorrent"
              disabled={isReadOnly}
            />
            <SelectInput
              label="Client Type"
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
              label="Host"
              value={clientForm.host || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, host: v });
              }}
              placeholder="localhost"
              disabled={isReadOnly}
            />
            <NumberInput
              label="Port"
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
              label="Use SSL"
              checked={clientForm.useSsl ?? false}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, useSsl: v });
              }}
              disabled={isReadOnly}
            />
            <TextInput
              label="Username"
              value={clientForm.username || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, username: v });
              }}
              disabled={isReadOnly}
            />
            <TextInput
              label="Password"
              value={isReadOnly ? "••••••••••••" : clientForm.password || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, password: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <TextInput
              label="Category"
              value={clientForm.category || ""}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, category: v });
              }}
              hint="Filter by category"
              disabled={isReadOnly}
            />
            <Toggle
              label="Enabled"
              checked={clientForm.enable ?? true}
              onChange={(v) => {
                setClientTestResult(null);
                setClientForm({ ...clientForm, enable: v });
              }}
              disabled={isReadOnly}
            />

            {renderTestAlert(testClientMutation.isPending, clientTestResult, clientForm.host || "client")}

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
                {testClientMutation.isPending ? "Testing..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  Previous
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleSaveClient}
                    disabled={createClientMutation.isPending}
                  >
                    {createClientMutation.isPending ? "Saving..." : clientSaved ? "Saved ✓ Next" : "Save & Next"}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    Next
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 2: Prowlarr Indexer Form (Matches Screenshot 2) */}
        {/* ========================================================================= */}
        {currentStep === 2 && (
          <div>
            <TextInput
              label="Name"
              value={indexerForm.name || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, name: v });
              }}
              placeholder="Prowlarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label="Type"
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
              label="URL"
              value={indexerForm.url || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, url: v });
              }}
              placeholder="http://prowlarr:9696"
              disabled={isReadOnly}
            />
            <TextInput
              label="API Key"
              value={isReadOnly ? "••••••••••••••••••••••••••••••••" : indexerForm.apiKey || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <TextInput
              label="API Path"
              value={indexerForm.apiPath || "/api"}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, apiPath: v });
              }}
              placeholder="/api"
              disabled={isReadOnly}
            />
            <TextInput
              label="Categories"
              value={indexerForm.categories || ""}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, categories: v });
              }}
              placeholder="2000,5000"
              disabled={isReadOnly}
            />
            <Toggle
              label="Enable"
              checked={indexerForm.enable ?? true}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="RSS"
              checked={indexerForm.enableRss ?? true}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, enableRss: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Search"
              checked={indexerForm.enableSearch ?? true}
              onChange={(v) => {
                setIndexerTestResult(null);
                setIndexerForm({ ...indexerForm, enableSearch: v });
              }}
              disabled={isReadOnly}
            />

            {renderTestAlert(testIndexerMutation.isPending, indexerTestResult, indexerForm.url || "Prowlarr")}

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
                {testIndexerMutation.isPending ? "Testing..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  Previous
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleSaveIndexer}
                    disabled={createIndexerMutation.isPending}
                  >
                    {createIndexerMutation.isPending ? "Saving..." : indexerSaved ? "Saved ✓ Next" : "Save & Next"}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    Next
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 3: Sonarr Connection Form (Matches Screenshot 1) */}
        {/* ========================================================================= */}
        {currentStep === 3 && (
          <div>
            <TextInput
              label="Name"
              value={sonarrForm.name || ""}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, name: v });
              }}
              placeholder="Sonarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label="Type"
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
              label="URL"
              value={sonarrForm.url || ""}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, url: v });
              }}
              placeholder="http://localhost:8989"
              disabled={isReadOnly}
            />
            <TextInput
              label="API Key"
              value={isReadOnly ? "••••••••••••••••••••••••••••••••" : sonarrForm.apiKey || ""}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <Toggle
              label="Enable Connection"
              checked={sonarrForm.enable ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Sync Enabled"
              checked={sonarrForm.syncEnabled ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, syncEnabled: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Auto Add"
              checked={sonarrForm.enableAutomaticAdd ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, enableAutomaticAdd: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Webhook"
              checked={sonarrForm.webhookEnabled ?? true}
              onChange={(v) => {
                setSonarrTestResult(null);
                setSonarrForm({ ...sonarrForm, webhookEnabled: v });
              }}
              disabled={isReadOnly}
            />
            {sonarrForm.webhookEnabled !== false && (
              <TextInput
                label="Webhook Host"
                value={sonarrForm.webhookHost || ""}
                onChange={(v) => {
                  setSonarrTestResult(null);
                  setSonarrForm({ ...sonarrForm, webhookHost: v });
                }}
                placeholder="seedarr"
                hint="Hostname or IP for *arr to reach Seedarr (leave empty to use default)"
                disabled={isReadOnly}
              />
            )}

            {renderTestAlert(testArrMutation.isPending, sonarrTestResult, sonarrForm.url || "Sonarr")}

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
                {testArrMutation.isPending ? "Testing..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  Previous
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={() => handleSaveArr(sonarrForm, setSonarrSaved, "Sonarr")}
                    disabled={createArrMutation.isPending}
                  >
                    {createArrMutation.isPending ? "Saving..." : sonarrSaved ? "Saved ✓ Next" : "Save & Next"}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    Next
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 4: Radarr Connection Form (Matches Screenshot 1) */}
        {/* ========================================================================= */}
        {currentStep === 4 && (
          <div>
            <TextInput
              label="Name"
              value={radarrForm.name || ""}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, name: v });
              }}
              placeholder="Radarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label="Type"
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
              label="URL"
              value={radarrForm.url || ""}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, url: v });
              }}
              placeholder="http://localhost:7878"
              disabled={isReadOnly}
            />
            <TextInput
              label="API Key"
              value={isReadOnly ? "••••••••••••••••••••••••••••••••" : radarrForm.apiKey || ""}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <Toggle
              label="Enable Connection"
              checked={radarrForm.enable ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Sync Enabled"
              checked={radarrForm.syncEnabled ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, syncEnabled: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Auto Add"
              checked={radarrForm.enableAutomaticAdd ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, enableAutomaticAdd: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Webhook"
              checked={radarrForm.webhookEnabled ?? true}
              onChange={(v) => {
                setRadarrTestResult(null);
                setRadarrForm({ ...radarrForm, webhookEnabled: v });
              }}
              disabled={isReadOnly}
            />
            {radarrForm.webhookEnabled !== false && (
              <TextInput
                label="Webhook Host"
                value={radarrForm.webhookHost || ""}
                onChange={(v) => {
                  setRadarrTestResult(null);
                  setRadarrForm({ ...radarrForm, webhookHost: v });
                }}
                placeholder="seedarr"
                hint="Hostname or IP for *arr to reach Seedarr (leave empty to use default)"
                disabled={isReadOnly}
              />
            )}

            {renderTestAlert(testArrMutation.isPending, radarrTestResult, radarrForm.url || "Radarr")}

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
                {testArrMutation.isPending ? "Testing..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  Previous
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={() => handleSaveArr(radarrForm, setRadarrSaved, "Radarr")}
                    disabled={createArrMutation.isPending}
                  >
                    {createArrMutation.isPending ? "Saving..." : radarrSaved ? "Saved ✓ Next" : "Save & Next"}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    Next
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ========================================================================= */}
        {/* STEP 5: Lidarr Connection Form (Matches Screenshot 1) */}
        {/* ========================================================================= */}
        {currentStep === 5 && (
          <div>
            <TextInput
              label="Name"
              value={lidarrForm.name || ""}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, name: v });
              }}
              placeholder="Lidarr"
              disabled={isReadOnly}
            />
            <SelectInput
              label="Type"
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
              label="URL"
              value={lidarrForm.url || ""}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, url: v });
              }}
              placeholder="http://localhost:8686"
              disabled={isReadOnly}
            />
            <TextInput
              label="API Key"
              value={isReadOnly ? "••••••••••••••••••••••••••••••••" : lidarrForm.apiKey || ""}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, apiKey: v });
              }}
              type="password"
              disabled={isReadOnly}
            />
            <Toggle
              label="Enable Connection"
              checked={lidarrForm.enable ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, enable: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Sync Enabled"
              checked={lidarrForm.syncEnabled ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, syncEnabled: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Auto Add"
              checked={lidarrForm.enableAutomaticAdd ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, enableAutomaticAdd: v });
              }}
              disabled={isReadOnly}
            />
            <Toggle
              label="Webhook"
              checked={lidarrForm.webhookEnabled ?? true}
              onChange={(v) => {
                setLidarrTestResult(null);
                setLidarrForm({ ...lidarrForm, webhookEnabled: v });
              }}
              disabled={isReadOnly}
            />
            {lidarrForm.webhookEnabled !== false && (
              <TextInput
                label="Webhook Host"
                value={lidarrForm.webhookHost || ""}
                onChange={(v) => {
                  setLidarrTestResult(null);
                  setLidarrForm({ ...lidarrForm, webhookHost: v });
                }}
                placeholder="seedarr"
                hint="Hostname or IP for *arr to reach Seedarr (leave empty to use default)"
                disabled={isReadOnly}
              />
            )}

            {renderTestAlert(testArrMutation.isPending, lidarrTestResult, lidarrForm.url || "Lidarr")}

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
                {testArrMutation.isPending ? "Testing..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handlePrev}
                >
                  Previous
                </button>
                {mode === "interactive" ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={() => handleSaveArr(lidarrForm, setLidarrSaved, "Lidarr")}
                    disabled={createArrMutation.isPending}
                  >
                    {createArrMutation.isPending ? "Saving..." : lidarrSaved ? "Saved ✓ Next" : "Save & Next"}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-small"
                    onClick={handleNext}
                  >
                    Next
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
              Your connections are set! Seedarr is ready to harvest swarm trackers, coordinate seeding,
              and sync with your media library.
            </p>

            <div style={{ display: "flex", justifyContent: "center", gap: "0.75rem", flexWrap: "wrap" }}>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={() => {
                  handleClose();
                  navigate("/");
                }}
                style={{ padding: "0.45rem 1.25rem" }}
              >
                📊 Go to Dashboard
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
                📦 View Torrents
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
                ⚙️ Settings
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
            borderTop: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
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
            <span>Don't show this guide on startup</span>
          </label>

          <span>
            Step {currentStep + 1} of {STEPS.length}
          </span>
        </div>
      </div>
    </div>
  );
}
