import fetcher from "../auth/httpClient";
import {
  ISubscriptionPolicy,
  ISubscriptionPolicyDraft,
  ISubscriptionPolicySimulation,
} from "./types";

const policyUrl = (feedId: string) =>
  `/api/subscription-policies/${encodeURIComponent(feedId)}`;

export const getSubscriptionPolicy = async (feedId: string) =>
  await fetcher<ISubscriptionPolicy>(policyUrl(feedId));

export const saveSubscriptionPolicy = async (
  feedId: string,
  policy: ISubscriptionPolicyDraft,
) =>
  await fetcher<ISubscriptionPolicy>(policyUrl(feedId), {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(policy),
  });

export const deleteSubscriptionPolicy = async (feedId: string) =>
  await fetcher<void>(policyUrl(feedId), { method: "DELETE" });

export const simulateSubscriptionPolicy = async (
  feedId: string,
  policy: ISubscriptionPolicyDraft,
) =>
  await fetcher<ISubscriptionPolicySimulation>(
    `${policyUrl(feedId)}/simulate`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(policy),
    },
  );
