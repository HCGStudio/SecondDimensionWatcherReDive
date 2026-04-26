export interface IWebDavToken {
  id: string;
  username: string;
  description?: string;
  createdAt: string;
}

export interface ICreateWebDavTokenResponse {
  id: string;
  username: string;
  token: string;
  description?: string;
  createdAt: string;
}
