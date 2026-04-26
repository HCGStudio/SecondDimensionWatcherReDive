import zhCnAnimation from "./locales/zh-CN/animation.json";
import zhCnAuth from "./locales/zh-CN/auth.json";
import zhCnChat from "./locales/zh-CN/chat.json";
import zhCnCommon from "./locales/zh-CN/common.json";
import zhCnErrors from "./locales/zh-CN/errors.json";
import zhCnFeeds from "./locales/zh-CN/feeds.json";
import zhCnFiles from "./locales/zh-CN/files.json";
import zhCnPlayer from "./locales/zh-CN/player.json";
import zhCnSeason from "./locales/zh-CN/season.json";
import zhCnTasks from "./locales/zh-CN/tasks.json";

import enAnimation from "./locales/en/animation.json";
import enAuth from "./locales/en/auth.json";
import enChat from "./locales/en/chat.json";
import enCommon from "./locales/en/common.json";
import enErrors from "./locales/en/errors.json";
import enFeeds from "./locales/en/feeds.json";
import enFiles from "./locales/en/files.json";
import enPlayer from "./locales/en/player.json";
import enSeason from "./locales/en/season.json";
import enTasks from "./locales/en/tasks.json";

import jaAnimation from "./locales/ja/animation.json";
import jaAuth from "./locales/ja/auth.json";
import jaChat from "./locales/ja/chat.json";
import jaCommon from "./locales/ja/common.json";
import jaErrors from "./locales/ja/errors.json";
import jaFeeds from "./locales/ja/feeds.json";
import jaFiles from "./locales/ja/files.json";
import jaPlayer from "./locales/ja/player.json";
import jaSeason from "./locales/ja/season.json";
import jaTasks from "./locales/ja/tasks.json";

export const resources = {
  "zh-cn": {
    common: zhCnCommon,
    auth: zhCnAuth,
    errors: zhCnErrors,
    animation: zhCnAnimation,
    files: zhCnFiles,
    chat: zhCnChat,
    feeds: zhCnFeeds,
    season: zhCnSeason,
    tasks: zhCnTasks,
    player: zhCnPlayer,
  },
  en: {
    common: enCommon,
    auth: enAuth,
    errors: enErrors,
    animation: enAnimation,
    files: enFiles,
    chat: enChat,
    feeds: enFeeds,
    season: enSeason,
    tasks: enTasks,
    player: enPlayer,
  },
  ja: {
    common: jaCommon,
    auth: jaAuth,
    errors: jaErrors,
    animation: jaAnimation,
    files: jaFiles,
    chat: jaChat,
    feeds: jaFeeds,
    season: jaSeason,
    tasks: jaTasks,
    player: jaPlayer,
  },
} as const;
