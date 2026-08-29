import { IAuthProfile, UserRole } from "../auth/IAuthResult";

export interface IAccountSession {
  id: string;
  userId: string;
  username: string;
  profileId: string;
  profileName: string;
  deviceName?: string;
  authenticatedAt: string;
  createdAt: string;
  lastSeenAt: string;
  expiresAt: string;
  revokedAt?: string;
  isCurrent: boolean;
}

export interface IUserAccount {
  id: string;
  username: string;
  role: UserRole;
  isDisabled: boolean;
  createdAt: string;
  profiles: IAuthProfile[];
}
