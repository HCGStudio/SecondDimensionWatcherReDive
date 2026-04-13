export interface TaskMetadata {
  name: string;
  description: string;
}

export const TASK_METADATA: Record<string, TaskMetadata> = {
  SyncFeed: {
    name: "SyncFeed",
    description: "同步 RSS 订阅",
  },
  ScrapeSeasonBangumi: {
    name: "ScrapeSeasonBangumi",
    description: "更新当季番组列表",
  },
  InferAnimationMetadata: {
    name: "InferAnimationMetadata",
    description: "AI 元数据推断",
  },
};

export function getTaskMetadata(id: string): TaskMetadata {
  return TASK_METADATA[id] ?? { name: id, description: "" };
}
