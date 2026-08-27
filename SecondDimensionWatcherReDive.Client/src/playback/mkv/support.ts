import { Input, MATROSKA, UrlSource } from "mediabunny";

export interface MkvPlaybackProbe {
  videoCodec: string;
  audioCodec: string | null;
  videoDecodable: boolean;
  audioDecodable: boolean;
}

const abortError = (): DOMException =>
  new DOMException("The operation was aborted", "AbortError");

export const isAbortError = (error: unknown): boolean =>
  error instanceof DOMException && error.name === "AbortError";

export const isMkvPath = (path: string): boolean =>
  /\.mkv(?:$|[?#])/i.test(path);

/**
 * Only AVC is copied into the fallback MP4. Other WebCodecs-decodable codecs
 * may be legal in Matroska but unsupported by the MP4 muxer or HTML video.
 */
export const canCopyVideoCodecToMp4 = (
  codec: string | null | undefined,
): boolean => {
  const normalized = codec
    ?.trim()
    .toLowerCase()
    .replace(/[\s._-]/g, "");
  return (
    normalized === "avc" ||
    normalized === "h264" ||
    normalized?.startsWith("avc1") === true
  );
};

/**
 * Reads only the Matroska metadata needed to choose the playback path. The
 * UrlSource uses HTTP Range requests and Input.dispose() cancels outstanding
 * requests, so probing does not download the complete episode.
 */
export const probeMkvPlayback = async (
  url: string,
  signal: AbortSignal,
): Promise<MkvPlaybackProbe> => {
  if (signal.aborted) throw abortError();

  const input = new Input({
    formats: [MATROSKA],
    source: new UrlSource(url, {
      maxCacheSize: 8 * 1024 * 1024,
      parallelism: 2,
    }),
  });
  const onAbort = () => input.dispose();
  signal.addEventListener("abort", onAbort, { once: true });

  try {
    const videoTrack = await input.getPrimaryVideoTrack();
    if (!videoTrack) throw new Error("The MKV file has no video track");

    const audioTrack = await videoTrack.getPrimaryPairableAudioTrack();
    const [videoCodec, audioCodec, videoDecodable, audioDecodable] =
      await Promise.all([
        videoTrack.getCodec(),
        audioTrack?.getCodec() ?? Promise.resolve(null),
        videoTrack.canDecode(),
        audioTrack?.canDecode() ?? Promise.resolve(true),
      ]);

    if (signal.aborted) throw abortError();

    return {
      videoCodec:
        videoCodec ??
        String((await videoTrack.getInternalCodecId()) ?? "unknown"),
      audioCodec:
        audioCodec ??
        (audioTrack
          ? String((await audioTrack.getInternalCodecId()) ?? "unknown")
          : null),
      videoDecodable,
      audioDecodable,
    };
  } catch (error) {
    if (signal.aborted) throw abortError();
    throw error;
  } finally {
    signal.removeEventListener("abort", onAbort);
    input.dispose();
  }
};
