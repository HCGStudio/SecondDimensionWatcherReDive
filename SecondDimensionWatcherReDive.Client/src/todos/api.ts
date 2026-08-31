import fetcher from "../auth/httpClient";
import { TodoStateAction } from "./types";

export const updateTodoState = (
  keys: string[],
  action: TodoStateAction,
  snoozedUntil?: string,
) =>
  fetcher<void>("/api/todos/state", {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ keys, action, snoozedUntil }),
  });
