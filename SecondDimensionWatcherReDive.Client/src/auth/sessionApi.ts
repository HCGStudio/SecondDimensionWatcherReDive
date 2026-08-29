import { IAuthResult } from "./IAuthResult";

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
  if (!response.ok) throw new Error(`${response.status}`);
  return (await response.json()) as IAuthResult;
};
