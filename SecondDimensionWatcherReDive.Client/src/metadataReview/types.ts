export type MetadataReviewStatus = "pending" | "lowConfidence" | "failed";

export interface MetadataReviewMetadata {
  tmdbId: string | null;
  name: string | null;
  originalName: string | null;
  posterPath: string | null;
  season: number | null;
  episode: number | null;
  groupName: string | null;
}

export interface MetadataReviewItem {
  id: string;
  title: string;
  description: string | null;
  publishTime: string;
  reviewStatus: MetadataReviewStatus;
  confidence: number | null;
  failureReason: string | null;
  aiRetryCount: number;
  metadata: MetadataReviewMetadata;
  isDownloadFinished: boolean;
  mappedFileCount: number;
  revision: number;
  currentOperationId?: string | null;
}

export interface MetadataReviewCounts {
  pending: number;
  lowConfidence: number;
  failed: number;
}

export interface MetadataReviewOperation {
  operationId: string;
  itemId: string;
  title: string;
  appliedAt: string;
  revision: number;
  canUndo: boolean;
}

export interface MetadataReviewResponse {
  data: MetadataReviewItem[];
  totalItems: number;
  counts: MetadataReviewCounts;
  recentOperations: MetadataReviewOperation[];
}

export interface EditableMetadata {
  tmdbId: string | null;
  season: number | null;
  episode: number | null;
  groupName: string | null;
}

export type PathChangeKind = "added" | "moved" | "unchanged" | "removed";

export interface MetadataPathChange {
  fileName: string;
  currentVirtualPath: string | null;
  proposedVirtualPath: string | null;
  changeKind: PathChangeKind;
  collisionAdjusted: boolean;
}

export interface MetadataReviewPreview {
  previewId: string;
  baseRevision: number;
  resolvedMetadata: MetadataReviewMetadata;
  pathChanges: MetadataPathChange[];
  warnings: string[];
  canApply: boolean;
  expiresAt: string;
}

export interface MetadataRemapResult {
  operationId: string;
  revision: number;
  pathChanges: MetadataPathChange[];
  appliedAt: string;
  canUndo: boolean;
}
