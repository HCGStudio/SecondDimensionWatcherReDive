export interface IWebDavToken {
  id: string;
  userId: string;
  username: string;
  description?: string;
  createdAt: string;
  scope: string;
  virtualRoot: string;
  expiresAt?: string;
  revokedAt?: string;
}

export interface ICreateWebDavTokenResponse {
  id: string;
  username: string;
  token: string;
  description?: string;
  createdAt: string;
  userId: string;
  scope: string;
  virtualRoot: string;
  expiresAt: string;
}
