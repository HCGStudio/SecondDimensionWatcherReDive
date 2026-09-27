import { ApiError, apiErrorFromResponse } from "../errors/apiError";
import { IAuthResult } from "./IAuthResult";
import fetcher, {
  AuthIdentityChangedError,
  beginAuthBoundRequest,
  getAuthIdentityKey,
  rotateAuthenticatedSession,
} from "./httpClient";

export interface PasskeyInfo {
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface PasskeySettings {
  hasPassword: boolean;
  passkeys: PasskeyInfo[];
}

type CredentialDescriptorJSON = Omit<PublicKeyCredentialDescriptor, "id"> & {
  id: string;
};
type CreationOptionsJSON = Omit<
  PublicKeyCredentialCreationOptions,
  "challenge" | "user" | "excludeCredentials"
> & {
  challenge: string;
  user: Omit<PublicKeyCredentialUserEntity, "id"> & { id: string };
  excludeCredentials?: CredentialDescriptorJSON[];
};
type RequestOptionsJSON = Omit<
  PublicKeyCredentialRequestOptions,
  "challenge" | "allowCredentials"
> & {
  challenge: string;
  allowCredentials?: CredentialDescriptorJSON[];
};
interface Ceremony<T> {
  ceremonyId: string;
  publicKey: T;
}

export const isPasskeyHttps = () =>
  typeof window !== "undefined" &&
  window.location.protocol === "https:" &&
  window.isSecureContext;

export const isPasskeySupported = () =>
  isPasskeyHttps() &&
  typeof PublicKeyCredential !== "undefined" &&
  typeof navigator.credentials?.create === "function" &&
  typeof navigator.credentials?.get === "function";

const requirePasskeys = () => {
  if (!isPasskeyHttps()) throw new ApiError("passkeys_https_required", 0);
  if (!isPasskeySupported()) throw new ApiError("passkeys_not_supported", 0);
};

const decode = (value: string): ArrayBuffer => {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const bytes = atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, "="));
  return Uint8Array.from(bytes, (character) => character.charCodeAt(0)).buffer;
};

const encode = (value: ArrayBuffer): string => {
  let binary = "";
  for (const byte of new Uint8Array(value)) binary += String.fromCharCode(byte);
  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
};

const descriptor = (
  value: CredentialDescriptorJSON,
): PublicKeyCredentialDescriptor => ({
  ...value,
  id: decode(value.id),
});

const serializeCredential = (value: Credential | null) => {
  if (!(value instanceof PublicKeyCredential)) {
    throw new DOMException("Passkey ceremony canceled", "NotAllowedError");
  }
  const response = value.response;
  const common = {
    id: value.id,
    rawId: encode(value.rawId),
    type: value.type,
    authenticatorAttachment: value.authenticatorAttachment,
    clientExtensionResults: value.getClientExtensionResults(),
  };
  if (response instanceof AuthenticatorAttestationResponse) {
    return {
      ...common,
      response: {
        clientDataJSON: encode(response.clientDataJSON),
        attestationObject: encode(response.attestationObject),
        transports: response.getTransports?.() ?? [],
      },
    };
  }
  const assertion = response as AuthenticatorAssertionResponse;
  return {
    ...common,
    response: {
      clientDataJSON: encode(assertion.clientDataJSON),
      authenticatorData: encode(assertion.authenticatorData),
      signature: encode(assertion.signature),
      userHandle: assertion.userHandle ? encode(assertion.userHandle) : null,
    },
  };
};

const jsonRequest = (body: unknown, signal?: AbortSignal): RequestInit => ({
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body),
  signal,
});

const getAssertion = async (options: RequestOptionsJSON, signal: AbortSignal) =>
  serializeCredential(
    await navigator.credentials.get({
      publicKey: {
        ...options,
        challenge: decode(options.challenge),
        allowCredentials: options.allowCredentials?.map(descriptor),
      },
      signal,
    }),
  );

export async function registerPasskey(
  name: string,
  signal?: AbortSignal,
): Promise<void> {
  requirePasskeys();
  const bound = beginAuthBoundRequest(true, signal);
  try {
    const ceremony = await fetcher<Ceremony<CreationOptionsJSON>>(
      "/api/auth/passkeys/registration/options",
      jsonRequest({}, bound.signal),
    );
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
    const credential = serializeCredential(
      await navigator.credentials.create({
        publicKey: {
          ...ceremony.publicKey,
          challenge: decode(ceremony.publicKey.challenge),
          user: {
            ...ceremony.publicKey.user,
            id: decode(ceremony.publicKey.user.id),
          },
          excludeCredentials:
            ceremony.publicKey.excludeCredentials?.map(descriptor),
        },
        signal: bound.signal,
      }),
    );
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
    await fetcher(
      "/api/auth/passkeys/registration",
      jsonRequest(
        {
          ceremonyId: ceremony.ceremonyId,
          credential,
          name,
        },
        bound.signal,
      ),
    );
  } finally {
    bound.dispose();
  }
}

async function provePasskey(
  purpose: "reauthenticate" | "password-removal",
  signal?: AbortSignal,
) {
  requirePasskeys();
  const bound = beginAuthBoundRequest(true, signal);
  try {
    const path = `/api/auth/passkeys/${purpose}`;
    const ceremony = await fetcher<Ceremony<RequestOptionsJSON>>(
      `${path}/options`,
      jsonRequest({}, bound.signal),
    );
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
    const credential = await getAssertion(ceremony.publicKey, bound.signal);
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
    const body = { ceremonyId: ceremony.ceremonyId, credential };
    if (purpose === "reauthenticate") {
      await rotateAuthenticatedSession(path, (auth) => {
        // The shared refresh-token lock can wait while another tab signs out.
        if (!bound.isCurrent()) throw new AuthIdentityChangedError();
        return { ...body, refreshToken: auth.refreshToken };
      });
    } else {
      await fetcher(path, jsonRequest(body, bound.signal));
    }
  } finally {
    bound.dispose();
  }
}

export const reauthenticateWithPasskey = (signal?: AbortSignal) =>
  provePasskey("reauthenticate", signal);

export const removePasswordWithPasskey = (signal?: AbortSignal) =>
  provePasskey("password-removal", signal);

export async function loginWithPasskey(
  username: string,
  signal: AbortSignal,
): Promise<IAuthResult> {
  requirePasskeys();
  const identity = getAuthIdentityKey();
  const post = async <T>(path: string, body: unknown): Promise<T> => {
    if (identity !== getAuthIdentityKey()) throw new AuthIdentityChangedError();
    const response = await fetch(path, jsonRequest(body, signal));
    if (!response.ok) throw await apiErrorFromResponse(response);
    const result = (await response.json()) as T;
    if (identity !== getAuthIdentityKey() || signal.aborted)
      throw new AuthIdentityChangedError();
    return result;
  };
  const ceremony = await post<Ceremony<RequestOptionsJSON>>(
    "/api/auth/passkeys/login/options",
    { username },
  );
  const credential = await getAssertion(ceremony.publicKey, signal);
  return post<IAuthResult>("/api/auth/passkeys/login", {
    ceremonyId: ceremony.ceremonyId,
    credential,
    deviceName: navigator.userAgent.slice(0, 128),
  });
}

export const isPasskeyCancellation = (error: unknown) =>
  error instanceof DOMException &&
  ["NotAllowedError", "AbortError"].includes(error.name);
