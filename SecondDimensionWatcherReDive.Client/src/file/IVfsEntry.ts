export interface IVfsEntry {
  name: string;
  isDirectory: boolean;
  size?: number | null;
  lastModifiedUtc?: string | null;
}
