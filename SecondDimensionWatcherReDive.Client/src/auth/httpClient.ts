import { IAuthResult } from "./IAuthResult";
import { refreshJwtToken } from "./utils";

let authResult: IAuthResult | null = null;

export const setAuthResult = (result: IAuthResult) => {
  if (result && result.success) {
    authResult = result;
    localStorage.setItem("auth", JSON.stringify(result));
  }
};

export default async function fetcher<JSON = any>(
  input: RequestInfo,
  init?: RequestInit,
): Promise<JSON> {
  if (authResult) {
    const res = await fetch(input, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${authResult.token}`,
      },
    });

    if (res.status !== 401) {
      return await res.json();
    }

    //Token expired
    setAuthResult(await refreshJwtToken(authResult));

    //Try again
    const tryAgain = await fetch(input, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${authResult.token}`,
      },
    });

    return await tryAgain.json();
  }

  if (localStorage.getItem("auth")) {
    authResult = JSON.parse(localStorage.getItem("auth")!);
    //Let upper handle
    return await fetcher(input, init);
  }

  //Give up auth
  const res = await fetch(input, init);
  return res.json();
}
