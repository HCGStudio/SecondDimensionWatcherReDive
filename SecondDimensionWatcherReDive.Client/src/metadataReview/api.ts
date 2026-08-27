import useSWR from "swr";

import fetcher from "../auth/httpClient";
import {
  EditableMetadata,
  MetadataRemapResult,
  MetadataReviewPreview,
  MetadataReviewResponse,
  MetadataReviewStatus,
} from "./types";

export const METADATA_REVIEW_PAGE_SIZE = 20;

export function useMetadataReview(status: MetadataReviewStatus, page: number) {
  const skip = (page - 1) * METADATA_REVIEW_PAGE_SIZE;
  const query = new URLSearchParams({
    status,
    skip: String(skip),
    take: String(METADATA_REVIEW_PAGE_SIZE),
  });

  return useSWR<MetadataReviewResponse>(
    `/api/metadata-review?${query.toString()}`,
    fetcher,
  );
}

export function previewMetadataReview(
  id: string,
  expectedRevision: number,
  metadata: EditableMetadata,
): Promise<MetadataReviewPreview> {
  return fetcher(`/api/metadata-review/${encodeURIComponent(id)}/preview`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ expectedRevision, metadata }),
  });
}

export function applyMetadataReview(
  id: string,
  previewId: string,
): Promise<MetadataRemapResult> {
  return fetcher(`/api/metadata-review/${encodeURIComponent(id)}/apply`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ previewId }),
  });
}

export function undoMetadataRemap(
  operationId: string,
  expectedRevision: number,
): Promise<MetadataRemapResult> {
  return fetcher(
    `/api/metadata-review/remaps/${encodeURIComponent(operationId)}/undo`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ expectedRevision }),
    },
  );
}

export function metadataReviewErrorStatus(error: unknown): number | null {
  if (!(error instanceof Error)) return null;
  const match = error.message.match(/\b(\d{3})\b/);
  return match ? Number(match[1]) : null;
}
