export interface IMediaLibrarySource {
  id: string;
  path: string;
  isMonitoring: boolean;
  createdAt: string;
  lastScanAt: string | null;
  lastError: string | null;
  lastImportedCount: number;
  lastUpdatedCount: number;
  lastRemovedCount: number;
  lastSkippedCount: number;
  isScanning: boolean;
}

export interface ICreateMediaLibrarySourceRequest {
  path: string;
  isMonitoring: boolean;
}

export interface IUpdateMediaLibrarySourceRequest {
  isMonitoring: boolean;
}
