import React from "react";

import { UserRound } from "lucide-react";

import {
  authenticatedFetch,
  getAuthIdentityKey,
  subscribeToAuthChanges,
} from "../auth/httpClient";

export const ProfileAvatar: React.FC<{ src?: string; className?: string }> = ({
  src,
  className = "h-9 w-9",
}) => {
  const [image, setImage] = React.useState<{
    source: string;
    url: string;
  } | null>(null);
  const stored = Boolean(
    src?.match(/^\/api\/accounts\/profiles\/[0-9a-f-]+\/avatar(?:\?|$)/i),
  );
  React.useEffect(() => {
    if (!src || !stored) return;
    const identity = getAuthIdentityKey();
    const controller = new AbortController();
    let active = true;
    let objectUrl: string | undefined;
    const clear = () => {
      active = false;
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      setImage(null);
    };
    const unsubscribe = subscribeToAuthChanges(({ auth, profileChanged }) => {
      if (!auth || profileChanged) clear();
    });
    void authenticatedFetch(src, { signal: controller.signal })
      .then((response) => response.blob())
      .then((blob) => {
        if (!active || getAuthIdentityKey() !== identity) return;
        objectUrl = URL.createObjectURL(blob);
        setImage({ source: src, url: objectUrl });
      })
      .catch(() => {
        if (active) setImage(null);
      });
    return () => {
      unsubscribe();
      clear();
    };
  }, [src, stored]);
  // Legacy URL avatars remain readable; never send bearer credentials to them.
  const url = stored ? (image?.source === src ? image?.url : undefined) : src;
  return url ? (
    <img
      src={url}
      alt=""
      className={`${className} rounded-full object-cover`}
    />
  ) : (
    <UserRound size={22} aria-hidden="true" />
  );
};
