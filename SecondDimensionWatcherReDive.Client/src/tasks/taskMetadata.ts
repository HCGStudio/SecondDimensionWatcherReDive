import { useTranslation } from "react-i18next";

export interface TaskMetadata {
  name: string;
  description: string;
}

const KNOWN_TASK_IDS = [
  "SyncFeed",
  "ScrapeSeasonBangumi",
  "InferAnimationMetadata",
] as const;

export function useTaskMetadata(): (id: string) => TaskMetadata {
  const { t } = useTranslation("tasks");
  return (id: string): TaskMetadata => {
    if ((KNOWN_TASK_IDS as readonly string[]).includes(id)) {
      return {
        name: t(`metadata.${id}.name`),
        description: t(`metadata.${id}.description`),
      };
    }
    return { name: id, description: "" };
  };
}
