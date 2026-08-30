export interface IAuthResult {
  token: string;
  refreshToken: string;
  success: boolean;
  sessionId?: string;
  profileId?: string;
}

export type UserRole = "Admin" | "Member" | "Viewer";

export interface IAuthProfile {
  id: string;
  name: string;
  avatar?: string;
  hasPin: boolean;
  isDefault: boolean;
}

export interface IAuthState {
  userId: string;
  username: string;
  role: UserRole;
  sessionId: string;
  profileId: string;
  profiles: IAuthProfile[];
}
