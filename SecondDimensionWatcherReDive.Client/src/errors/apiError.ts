export interface ApiErrorPayload {
  code?: string;
  [key: string]: unknown;
}

export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  readonly payload: ApiErrorPayload | null;

  constructor(
    code: string,
    status: number,
    payload: ApiErrorPayload | null = null,
  ) {
    // Keep the legacy numeric message for callers that have not migrated yet,
    // while exposing stable structured fields for all new UI decisions.
    super(status > 0 ? String(status) : code);
    this.name = "ApiError";
    this.code = code;
    this.status = status;
    this.payload = payload;
  }
}

export async function apiErrorFromResponse(
  response: Response,
  fallbackCode = `http_${response.status}`,
): Promise<ApiError> {
  let payload: ApiErrorPayload | null = null;
  try {
    const value: unknown = await response.json();
    if (value && typeof value === "object") {
      payload = value as ApiErrorPayload;
    }
  } catch {
    // An error response is allowed to have no JSON body. The status-based code
    // remains stable and no untranslated server text reaches the UI.
  }

  const code =
    typeof payload?.code === "string" && payload.code
      ? payload.code
      : fallbackCode;
  return new ApiError(code, response.status, payload);
}

export function apiErrorStatus(error: unknown): number | null {
  if (error instanceof ApiError) return error.status;
  if (!(error instanceof Error)) return null;
  const match = error.message.match(/\b(\d{3})\b/);
  return match ? Number(match[1]) : null;
}
