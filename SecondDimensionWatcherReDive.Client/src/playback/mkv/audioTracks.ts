import type { Input, InputAudioTrack, InputVideoTrack } from "mediabunny";

import { normalizeLanguage } from "../languagePreferences";

/** Apply the same language selection to probing and proxy playback. */
export const getPreferredMkvAudioTrack = async (
  input: Input,
  preferredLanguage?: string | null,
  videoTrack?: InputVideoTrack | null,
): Promise<InputAudioTrack | null> => {
  const language = normalizeLanguage(preferredLanguage);
  if (language) {
    const query = {
      filter: async (track: InputAudioTrack) =>
        normalizeLanguage(await track.getLanguageCode()) === language,
    };
    const preferredTrack = videoTrack
      ? await videoTrack.getPrimaryPairableAudioTrack(query)
      : await input.getPrimaryAudioTrack(query);
    if (preferredTrack) return preferredTrack;
  }

  return videoTrack
    ? videoTrack.getPrimaryPairableAudioTrack()
    : input.getPrimaryAudioTrack();
};
