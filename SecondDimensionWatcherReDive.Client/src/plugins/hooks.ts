import useSWR from "swr";

import { getPlugins } from "./api";

export const usePlugins = () => useSWR("/api/plugins", getPlugins);
