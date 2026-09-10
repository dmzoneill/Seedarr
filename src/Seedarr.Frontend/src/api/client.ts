import type {
  Category,
  IdentityProviderDefinition,
  SslTestRequest,
  SslCertificateValidationResult,
} from "./types";

const BASE_URL = "/api/v1";

class ApiClient {
  private apiKey: string | null = null;

  setApiKey(key: string) {
    this.apiKey = key;
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
    options: RequestInit = {},
  ): Promise<T> {
    const headers: HeadersInit = {
      "Content-Type": "application/json",
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
      const errorMsg = await this.parseError(response);
      throw new Error(errorMsg);
    }

    if (
      response.status === 204 ||
      response.headers.get("content-length") === "0"
    ) {
      return null as unknown as T;
    }

    return response.json();
  }

  get<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: "GET" });
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
      const errorMsg = await this.parseError(response);
      throw new Error(errorMsg);
    }
    return response.json();
  }
}

export const apiClient = new ApiClient();
export const api = apiClient;
