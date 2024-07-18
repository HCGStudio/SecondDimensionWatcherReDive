import { IAuthResult } from "./IAuthResult";

export const login = async (password: string): Promise<IAuthResult> => {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password }),
  });
  return (await response.json()) as IAuthResult;
};

export const refreshJwtToken = async (
  oldToken: IAuthResult,
): Promise<IAuthResult> => {
  const response = await fetch("/api/auth/refresh", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(oldToken),
  });
  return (await response.json()) as IAuthResult;
};

export const register = async (
  password: string,
): Promise<IAuthResult | null> => {
  const response = await fetch("/api/auth/register", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password }),
  });
  return (await response.json()) as IAuthResult;
};
