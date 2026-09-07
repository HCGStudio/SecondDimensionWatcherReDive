export const TMDB_IMAGE_SIZES = [
  "w92",
  "w154",
  "w185",
  "w300",
  "w342",
  "w500",
  "w780",
  "original",
] as const;

export type TmdbImageSize = (typeof TMDB_IMAGE_SIZES)[number];

export function tmdbImageUrl(
  posterPath: string | null | undefined,
  size: TmdbImageSize = "w300",
): string | null {
  if (!posterPath) return null;
  const match = posterPath.match(
    /^\/?([A-Za-z0-9][A-Za-z0-9._-]{0,199}\.(?:avif|jpe?g|png|webp))$/i,
  );
  const fileName = match?.[1];
  if (!fileName || fileName.includes("..")) {
    return null;
  }
  return `/api/images/tmdb/${size}/${encodeURIComponent(fileName)}`;
}
