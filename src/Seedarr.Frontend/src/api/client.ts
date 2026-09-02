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
            const errors = (data as { errors?: Record<string, string[]> | string[] }).errors;
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
