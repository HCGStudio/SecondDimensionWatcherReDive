import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { ISubscriptionPolicy } from "./types";

export const useSubscriptionPolicies = () =>
  useSWR<ISubscriptionPolicy[]>("/api/subscription-policies", fetcher);
