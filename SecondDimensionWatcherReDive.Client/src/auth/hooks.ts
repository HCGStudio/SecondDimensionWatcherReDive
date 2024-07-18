import useSwr from "swr";

export const useAllowRegister = () =>
  useSwr<{ allow: boolean }>("/api/auth/allowRegister");
export const useLoginStatus = () => useSwr("/api/auth/verify");
