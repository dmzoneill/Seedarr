import { broadcastSessionExpired } from "../utils/authChannel";
import { trackException } from "../utils/analytics";
import type {
  Category,
  IdentityProviderDefinition,
  SslTestRequest,
  SslCertificateValidationResult,
  FileSystemResource,
  AuthProvider,
  CurrentUser,
  LoginRequest,
  CustomScriptTestRequest,
  CustomScriptTestResult,
  SetupStatus,
  SetupCompleteRequest,
  TorrentCreationRequest,
  TorrentCreationResult,
} from "./types";

declare global {
  interface Window {
    Seedarr?: {
      urlBase?: string;
      apiKey?: string;
    };
  }
}

export function getUrlBase(): string {
  if (typeof window !== "undefined" && window.Seedarr?.urlBase) {
    return window.Seedarr.urlBase.replace(/\/+$/, "");
  }
  return "";
}

export const BASE_URL = `${getUrlBase()}/api/v1`;

class ApiClient {
  private apiKey: string | null = null;

  setApiKey(key: string) {
    this.apiKey = key;
  }

  getStoredApiKey(): string | null {
    return (
      this.apiKey ||
      (typeof window !== "undefined" ? window.Seedarr?.apiKey : null) ||
      null
    );
  }

  private async parseError(response: Response): Promise<string> {
    let errorMessage = `API error: ${response.status} ${response.statusText}`;
    try {
      const text = await response.text();
      if (text) {
        try {
          const data = JSON.parse(text);
          if (typeof data === "string") {
            errorMessage = data;
          } else if (data && typeof data === "object") {
            const errors = (
              data as { errors?: Record<string, string[]> | string[] }
            ).errors;
            let formattedErrors: string | null = null;
            if (Array.isArray(errors)) {
              formattedErrors = errors.join(", ");
            } else if (errors && typeof errors === "object") {
              formattedErrors = Object.values(errors).flat().join(", ");
            }

            errorMessage =
              (data as { message?: string }).message ||
              (data as { title?: string }).title ||
              (data as { error?: string }).error ||
              formattedErrors ||
              errorMessage;
          }
        } catch {
          errorMessage = text;
        }
      }
    } catch {
      // Fallback
    }
    return errorMessage;
  }

  private async request<T>(
    endpoint: string,
    options: RequestInit & { responseType?: "blob" | "json" } = {},
  ): Promise<T> {
    const isBlob = options.responseType === "blob";
    const headers: HeadersInit = {
      ...(isBlob ? {} : { "Content-Type": "application/json" }),
      ...options.headers,
    };

    if (this.apiKey) {
      (headers as Record<string, string>)["X-Api-Key"] = this.apiKey;
    }

    const response = await fetch(`${BASE_URL}${endpoint}`, {
      ...options,
      headers,
    });

    if (!response.ok) {
      if (response.status === 401 && !endpoint.includes("/auth/login")) {
        broadcastSessionExpired();
        if (
          typeof window !== "undefined" &&
          window.location.pathname !== "/login"
        ) {
          const returnUrl = window.location.pathname + window.location.search;
          window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
        }
      }
      const errorMsg = await this.parseError(response);
      if (response.status >= 500) {
        const cleanEndpoint = endpoint.split("?")[0];
        trackException(
          `Backend ${response.status}: ${cleanEndpoint} - ${errorMsg.slice(0, 80)}`,
          false,
          "backend_api_5xx",
        );
      }
      throw new Error(errorMsg);
    }

    if (
      response.status === 204 ||
      response.headers.get("content-length") === "0"
    ) {
      return null as unknown as T;
    }

    if (isBlob) {
      return (await response.blob()) as unknown as T;
    }

    return response.json();
  }

  get<T>(
    endpoint: string,
    options?: RequestInit & { responseType?: "blob" | "json" },
  ): Promise<T> {
    return this.request<T>(endpoint, { ...options, method: "GET" });
  }

  async getWithHeaders<T>(
    endpoint: string,
    options?: RequestInit & { responseType?: "blob" | "json" },
  ): Promise<{ data: T; headers: Headers }> {
    const isBlob = options?.responseType === "blob";
    const headers: HeadersInit = {
      ...(isBlob ? {} : { "Content-Type": "application/json" }),
      ...options?.headers,
    };

    if (this.apiKey) {
      (headers as Record<string, string>)["X-Api-Key"] = this.apiKey;
    }

    const response = await fetch(`${BASE_URL}${endpoint}`, {
      ...options,
      method: "GET",
      headers,
    });

    if (!response.ok) {
      if (response.status === 401 && !endpoint.includes("/auth/login")) {
        broadcastSessionExpired();
        if (
          typeof window !== "undefined" &&
          window.location.pathname !== "/login"
        ) {
          const returnUrl = window.location.pathname + window.location.search;
          window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
        }
      }
      const errorMsg = await this.parseError(response);
      throw new Error(errorMsg);
    }

    let data: T;
    if (
      response.status === 204 ||
      response.headers.get("content-length") === "0"
    ) {
      data = null as unknown as T;
    } else if (isBlob) {
      data = (await response.blob()) as unknown as T;
    } else {
      data = await response.json();
    }

    return { data, headers: response.headers };
  }

  post<T>(endpoint: string, body?: unknown): Promise<T> {
    return this.request<T>(endpoint, {
      method: "POST",
      body: body ? JSON.stringify(body) : undefined,
    });
  }

  put<T>(endpoint: string, body?: unknown): Promise<T> {
    return this.request<T>(endpoint, {
      method: "PUT",
      body: body ? JSON.stringify(body) : undefined,
    });
  }

  delete<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: "DELETE" });
  }

  getApiKey(): Promise<{ apiKey: string }> {
    return this.get<{ apiKey: string }>("/config/general/api-key");
  }

  getIdProviders(): Promise<IdentityProviderDefinition[]> {
    return this.get<IdentityProviderDefinition[]>("/config/auth/providers");
  }

  getIdProvider(id: number): Promise<IdentityProviderDefinition> {
    return this.get<IdentityProviderDefinition>(`/config/auth/providers/${id}`);
  }

  createIdProvider(
    provider: Partial<IdentityProviderDefinition>,
  ): Promise<IdentityProviderDefinition> {
    return this.post<IdentityProviderDefinition>(
      "/config/auth/providers",
      provider,
    );
  }

  updateIdProvider(
    id: number,
    provider: Partial<IdentityProviderDefinition>,
  ): Promise<IdentityProviderDefinition> {
    return this.put<IdentityProviderDefinition>(
      `/config/auth/providers/${id}`,
      provider,
    );
  }

  deleteIdProvider(id: number): Promise<void> {
    return this.delete<void>(`/config/auth/providers/${id}`);
  }

  getCategories(): Promise<Category[]> {
    return this.get<Category[]>("/categories");
  }

  getCategory(id: number): Promise<Category> {
    return this.get<Category>(`/categories/${id}`);
  }

  createCategory(category: Partial<Category>): Promise<Category> {
    return this.post<Category>("/categories", category);
  }

  updateCategory(id: number, category: Partial<Category>): Promise<Category> {
    return this.put<Category>(`/categories/${id}`, category);
  }

  deleteCategory(id: number): Promise<void> {
    return this.delete<void>(`/categories/${id}`);
  }

  testIdProvider(
    provider: Partial<IdentityProviderDefinition>,
  ): Promise<{ success: boolean; message: string }> {
    return this.post<{ success: boolean; message: string }>(
      "/config/auth/providers/test",
      provider,
    );
  }

  testSsl(request: SslTestRequest): Promise<SslCertificateValidationResult> {
    return this.post<SslCertificateValidationResult>(
      "/config/general/test-ssl",
      request,
    );
  }

  testProxy(request: {
    proxyType: string;
    proxyHost: string;
    proxyPort: number;
    proxyAuthEnabled: boolean;
    proxyUsername?: string;
    proxyPassword?: string;
    testTargetHost?: string;
    testTargetPort?: number;
    timeoutMs?: number;
  }): Promise<{
    success: boolean;
    message: string;
    remoteDnsVerified?: boolean;
  }> {
    return this.post<{
      success: boolean;
      message: string;
      remoteDnsVerified?: boolean;
    }>("/config/network/test-proxy", request);
  }

  getFileSystem(
    path?: string,
    includeFiles?: boolean,
  ): Promise<FileSystemResource> {
    const params = new URLSearchParams();
    if (path) params.append("path", path);
    if (includeFiles) params.append("includeFiles", "true");
    const query = params.toString();
    return this.get<FileSystemResource>(
      `/filesystem${query ? `?${query}` : ""}`,
    );
  }

  createDirectory(path: string): Promise<{ success: boolean; path: string }> {
    return this.post<{ success: boolean; path: string }>("/filesystem/mkdir", {
      path,
    });
  }

  createTorrent(
    request: TorrentCreationRequest,
  ): Promise<TorrentCreationResult> {
    return this.post<TorrentCreationResult>("/torrents/create", request);
  }

  getAuthProviders(returnUrl?: string): Promise<AuthProvider[]> {
    const query = returnUrl
      ? `?returnUrl=${encodeURIComponent(returnUrl)}`
      : "";
    return this.get<AuthProvider[]>(`/auth/providers${query}`);
  }

  getCurrentUser(): Promise<CurrentUser> {
    return this.get<CurrentUser>("/auth/me");
  }

  login(request: LoginRequest): Promise<CurrentUser> {
    return this.post<CurrentUser>("/auth/login", request);
  }

  /**
   * Re-authenticates with retry for transient network connectivity issues.
   */
  async loginWithRetry(
    request: LoginRequest,
    maxRetries = 2,
  ): Promise<CurrentUser> {
    let lastError: unknown;
    for (let attempt = 0; attempt <= maxRetries; attempt++) {
      try {
        return await this.login(request);
      } catch (err: any) {
        lastError = err;
        const msg = String(err?.message || "");
        // Do not retry client/auth errors like invalid password
        if (
          msg.includes("401") ||
          msg.includes("400") ||
          msg.includes("Invalid credentials") ||
          msg.includes("required")
        ) {
          throw err;
        }
        if (attempt < maxRetries) {
          await new Promise((resolve) =>
            setTimeout(resolve, 500 * Math.pow(2, attempt)),
          );
        }
      }
    }
    throw lastError;
  }

  /**
   * Refreshes active session by probing /auth/me with retry for transient errors.
   */
  async refreshSession(maxRetries = 2): Promise<CurrentUser | null> {
    for (let attempt = 0; attempt <= maxRetries; attempt++) {
      try {
        const user = await this.getCurrentUser();
        return user;
      } catch (err: any) {
        if (err?.message?.includes("401")) {
          throw err;
        }
        if (attempt === maxRetries) {
          throw err;
        }
        await new Promise((resolve) =>
          setTimeout(resolve, 300 * Math.pow(2, attempt)),
        );
      }
    }
    return null;
  }

  logout(): Promise<{ message: string }> {
    return this.post<{ message: string }>("/auth/logout");
  }

  getSetupStatus(): Promise<SetupStatus> {
    return this.get<SetupStatus>("/system/setup/status");
  }

  completeSetup(payload: SetupCompleteRequest): Promise<{
    message: string;
    isSetupCompleted: boolean;
    isAuthEnabled: boolean;
  }> {
    return this.post<{
      message: string;
      isSetupCompleted: boolean;
      isAuthEnabled: boolean;
    }>("/system/setup/complete", payload);
  }

  testCustomScript(
    request: CustomScriptTestRequest,
  ): Promise<CustomScriptTestResult> {
    return this.post<CustomScriptTestResult>("/customscript/test", request);
  }

  async postForm<T>(endpoint: string, formData: FormData): Promise<T> {
    const headers: Record<string, string> = {};
    if (this.apiKey) {
      headers["X-Api-Key"] = this.apiKey;
    }
    const response = await fetch(`${BASE_URL}${endpoint}`, {
      method: "POST",
      headers,
      body: formData,
    });
    if (!response.ok) {
      if (response.status === 401 && !endpoint.includes("/auth/login")) {
        broadcastSessionExpired();
        if (
          typeof window !== "undefined" &&
          window.location.pathname !== "/login"
        ) {
          const returnUrl = window.location.pathname + window.location.search;
          window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
        }
      }
      const errorMsg = await this.parseError(response);
      throw new Error(errorMsg);
    }
    return response.json();
  }

  async renameTorrentFile(
    hash: string,
    oldPath: string,
    newPath: string,
  ): Promise<string> {
    const formData = new FormData();
    formData.append("hash", hash);
    formData.append("oldPath", oldPath);
    formData.append("newPath", newPath);
    const headers: Record<string, string> = {};
    if (this.apiKey) {
      headers["X-Api-Key"] = this.apiKey;
    }
    const response = await fetch("/api/v2/torrents/renameFile", {
      method: "POST",
      headers,
      body: formData,
    });
    if (!response.ok) {
      const errorMsg = await this.parseError(response);
      throw new Error(errorMsg);
    }
    return response.text();
  }

  async renameTorrentFolder(
    hash: string,
    oldPath: string,
    newPath: string,
  ): Promise<string> {
    const formData = new FormData();
    formData.append("hash", hash);
    formData.append("oldPath", oldPath);
    formData.append("newPath", newPath);
    const headers: Record<string, string> = {};
    if (this.apiKey) {
      headers["X-Api-Key"] = this.apiKey;
    }
    const response = await fetch("/api/v2/torrents/renameFolder", {
      method: "POST",
      headers,
      body: formData,
    });
    if (!response.ok) {
      const errorMsg = await this.parseError(response);
      throw new Error(errorMsg);
    }
    return response.text();
  }
}

export const apiClient = new ApiClient();
export const api = apiClient;
