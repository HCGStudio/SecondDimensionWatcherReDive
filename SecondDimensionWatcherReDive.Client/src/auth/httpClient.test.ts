import assert from "node:assert/strict";
import test from "node:test";

import { IAuthResult } from "./IAuthResult";

class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>();
  get length() {
    return this.values.size;
  }
  clear() {
    this.values.clear();
  }
  getItem(key: string) {
    return this.values.get(key) ?? null;
  }
  key(index: number) {
    return [...this.values.keys()][index] ?? null;
  }
  removeItem(key: string) {
    this.values.delete(key);
  }
  setItem(key: string, value: string) {
    this.values.set(key, value);
  }
}

class ExclusiveLocks {
  private tail: Promise<unknown> = Promise.resolve();

  request<T>(
    _name: string,
    _options: { mode: "exclusive" },
    callback: () => Promise<T>,
  ): Promise<T> {
    const result = this.tail.then(callback);
    this.tail = result.catch(() => undefined);
    return result;
  }
}

const stale: IAuthResult = {
  token: "access-a",
  refreshToken: "refresh-a",
  sessionId: "session",
  profileId: "profile-a",
  success: true,
};
const fresh: IAuthResult = {
  token: "access-b",
  refreshToken: "refresh-b",
  sessionId: "session",
  profileId: "profile-a",
  success: true,
};

test("cross-tab refresh lock serializes rotation and reuses the winner", async () => {
  const memoryStorage = new MemoryStorage();
  const windowTarget = new EventTarget() as EventTarget & {
    location: { pathname: string; href: string; assign(path: string): void };
  };
  windowTarget.location = {
    pathname: "/",
    href: "/",
    assign(path: string) {
      this.href = path;
    },
  };
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: memoryStorage,
  });
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: windowTarget,
  });
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: { locks: new ExclusiveLocks() },
  });
  Object.defineProperty(globalThis, "BroadcastChannel", {
    configurable: true,
    value: undefined,
  });
  if (typeof CustomEvent === "undefined") {
    class TestCustomEvent<T> extends Event {
      constructor(
        type: string,
        readonly init: CustomEventInit<T>,
      ) {
        super(type);
      }
      get detail() {
        return this.init.detail as T;
      }
    }
    Object.defineProperty(globalThis, "CustomEvent", {
      configurable: true,
      value: TestCustomEvent,
    });
  }

  let refreshCalls = 0;
  Object.defineProperty(globalThis, "fetch", {
    configurable: true,
    value: async () => {
      refreshCalls++;
      await Promise.resolve();
      return new Response(JSON.stringify(fresh), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    },
  });

  const auth = await import("./httpClient");
  auth.setAuthResult(stale);

  const [first, second] = await Promise.all([
    auth.refreshAuthSession(stale),
    auth.refreshAuthSession(stale),
  ]);

  assert.equal(refreshCalls, 1);
  assert.deepEqual(first, fresh);
  assert.deepEqual(second, fresh);
  assert.deepEqual(JSON.parse(memoryStorage.getItem("auth")!), fresh);
});

test("profile changes clear array-keyed caches then reload and logout redirects", async () => {
  const { applyAuthChange } = await import("./hooks");
  const calls: Array<{
    key: string | ((key: unknown) => boolean);
    options?: { revalidate?: boolean };
  }> = [];
  let reloaded = false;
  const mutate = async (
    key: string | ((key: unknown) => boolean),
    _data?: unknown,
    options?: { revalidate?: boolean },
  ) => {
    calls.push({ key, options });
  };

  await applyAuthChange(
    { auth: { ...fresh, profileId: "profile-b" }, profileChanged: true },
    mutate,
    undefined,
    () => {
      reloaded = true;
    },
  );

  assert.equal(calls.length, 1);
  assert.equal(typeof calls[0].key, "function");
  assert.equal(calls[0].options?.revalidate, false);
  const apiKeyPredicate = calls[0].key as (key: unknown) => boolean;
  assert.equal(apiKeyPredicate(["/api/chat/conversations", "profile-a"]), true);
  assert.equal(apiKeyPredicate(["settings", "profile-a"]), false);
  assert.equal(reloaded, true);

  let redirected = false;
  calls.length = 0;
  await applyAuthChange({ auth: null, profileChanged: true }, mutate, () => {
    redirected = true;
  });
  assert.equal(calls.length, 1);
  assert.equal(calls[0].options?.revalidate, false);
  assert.equal(redirected, true);
});

test("a late refresh response cannot overwrite a newer shared identity", async () => {
  const auth = await import("./httpClient");
  auth.setAuthResult(fresh);
  let finishRefresh: ((response: Response) => void) | undefined;
  Object.defineProperty(globalThis, "fetch", {
    configurable: true,
    value: () =>
      new Promise<Response>((resolve) => {
        finishRefresh = resolve;
      }),
  });

  const refresh = auth.refreshAuthSession(fresh);
  await Promise.resolve();
  const replacement: IAuthResult = {
    ...fresh,
    token: "access-new-session",
    refreshToken: "refresh-new-session",
    sessionId: "session-new",
    profileId: "profile-new",
  };
  // This is the shared-storage write made by another realm; intentionally do
  // not dispatch storage yet, reproducing the narrow response/notification race.
  localStorage.setItem("auth", JSON.stringify(replacement));
  finishRefresh?.(
    new Response(
      JSON.stringify({
        ...fresh,
        token: "late-access-a",
        refreshToken: "late-refresh-a",
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    ),
  );

  await assert.rejects(refresh, auth.AuthIdentityChangedError);
  assert.deepEqual(JSON.parse(localStorage.getItem("auth")!), replacement);

  // Restore the original realm for the remaining state-machine tests.
  localStorage.setItem("auth", JSON.stringify(fresh));
});

test("Viewer playback is read-only while writable roles retain profile mutations", async () => {
  const auth = await import("./httpClient");
  auth.setAuthResult(fresh);
  const identity = auth.getAuthIdentityKey();

  assert.equal(auth.canSendProfileMutation(identity, false), false);
  assert.equal(auth.canSendProfileMutation(identity, true), true);
});

test("late logout cleanup preserves a replacement login session", async () => {
  const auth = await import("./httpClient");
  auth.setAuthResult(fresh);

  assert.equal(auth.clearAuthForSession("an-older-session"), false);
  assert.deepEqual(JSON.parse(localStorage.getItem("auth")!), fresh);
});

test("external-tab storage profile change aborts streams and forbids 401 replay", async () => {
  const auth = await import("./httpClient");
  auth.setAuthResult(fresh);
  const oldIdentity = auth.getAuthIdentityKey();
  const stream = auth.beginAuthBoundRequest(true);
  let observedChange: import("./httpClient").AuthChangeDetail | undefined;
  const unsubscribe = auth.subscribeToAuthChanges((detail) => {
    observedChange = detail;
  });

  let finishFirstRequest: ((response: Response) => void) | undefined;
  let fetchCalls = 0;
  Object.defineProperty(globalThis, "fetch", {
    configurable: true,
    value: () => {
      fetchCalls += 1;
      return new Promise<Response>((resolve) => {
        finishFirstRequest = resolve;
      });
    },
  });

  const oldMutation = auth.default("/api/playback/progress", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: "{}",
  });
  await Promise.resolve();
  assert.equal(fetchCalls, 1);

  const remoteProfile: IAuthResult = {
    ...fresh,
    token: "access-profile-b",
    refreshToken: "refresh-profile-b",
    profileId: "profile-b",
  };
  localStorage.setItem("auth", JSON.stringify(remoteProfile));
  const storageEvent = new Event("storage");
  Object.defineProperty(storageEvent, "key", { value: "auth" });
  window.dispatchEvent(storageEvent);

  assert.equal(observedChange?.profileChanged, true);
  assert.equal(observedChange?.auth?.profileId, "profile-b");
  assert.equal(
    stream.signal.aborted,
    true,
    "the old chat/SSE signal is aborted",
  );
  assert.equal(auth.canSendProfileMutation(oldIdentity, true), false);
  assert.throws(
    () => auth.beginAuthBoundRequest(true),
    auth.AuthIdentityChangedError,
  );

  finishFirstRequest?.(new Response(null, { status: 401 }));
  await assert.rejects(oldMutation, auth.AuthIdentityChangedError);
  assert.equal(fetchCalls, 1, "401 was not refreshed or replayed as profile B");

  unsubscribe();
  stream.dispose();
});
