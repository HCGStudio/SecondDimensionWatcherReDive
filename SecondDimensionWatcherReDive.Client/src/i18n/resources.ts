import enAccounts from "./locales/en/accounts.json";
import enAnimation from "./locales/en/animation.json";
import enAuth from "./locales/en/auth.json";
import enCommon from "./locales/en/common.json";
import enErrors from "./locales/en/errors.json";
import enFeeds from "./locales/en/feeds.json";
import enFiles from "./locales/en/files.json";
import enIncidents from "./locales/en/incidents.json";
import enLibrary from "./locales/en/library.json";
import enMetadataReview from "./locales/en/metadataReview.json";
import enPlayer from "./locales/en/player.json";
import enSeason from "./locales/en/season.json";
import enTasks from "./locales/en/tasks.json";
import enTodos from "./locales/en/todos.json";
import enWatchlist from "./locales/en/watchlist.json";
import jaAccounts from "./locales/ja/accounts.json";
import jaAnimation from "./locales/ja/animation.json";
import jaAuth from "./locales/ja/auth.json";
import jaCommon from "./locales/ja/common.json";
import jaErrors from "./locales/ja/errors.json";
import jaFeeds from "./locales/ja/feeds.json";
import jaFiles from "./locales/ja/files.json";
import jaIncidents from "./locales/ja/incidents.json";
import jaLibrary from "./locales/ja/library.json";
import jaMetadataReview from "./locales/ja/metadataReview.json";
import jaPlayer from "./locales/ja/player.json";
import jaSeason from "./locales/ja/season.json";
import jaTasks from "./locales/ja/tasks.json";
import jaTodos from "./locales/ja/todos.json";
import jaWatchlist from "./locales/ja/watchlist.json";
import zhCnAccounts from "./locales/zh-CN/accounts.json";
import zhCnAnimation from "./locales/zh-CN/animation.json";
import zhCnAuth from "./locales/zh-CN/auth.json";
import zhCnCommon from "./locales/zh-CN/common.json";
import zhCnErrors from "./locales/zh-CN/errors.json";
import zhCnFeeds from "./locales/zh-CN/feeds.json";
import zhCnFiles from "./locales/zh-CN/files.json";
import zhCnIncidents from "./locales/zh-CN/incidents.json";
import zhCnLibrary from "./locales/zh-CN/library.json";
import zhCnMetadataReview from "./locales/zh-CN/metadataReview.json";
import zhCnPlayer from "./locales/zh-CN/player.json";
import zhCnSeason from "./locales/zh-CN/season.json";
import zhCnTasks from "./locales/zh-CN/tasks.json";
import zhCnTodos from "./locales/zh-CN/todos.json";
import zhCnWatchlist from "./locales/zh-CN/watchlist.json";

export const resources = {
  "zh-cn": {
    common: zhCnCommon,
    watchlist: zhCnWatchlist,
    accounts: zhCnAccounts,
    auth: zhCnAuth,
    errors: zhCnErrors,
    animation: zhCnAnimation,
    files: zhCnFiles,
    metadataReview: zhCnMetadataReview,
    feeds: zhCnFeeds,
    incidents: zhCnIncidents,
    library: zhCnLibrary,
    season: zhCnSeason,
    settings: {
      system: { reauthenticatePrompt: "请输入账户密码以确认此敏感操作" },
    },
    tasks: zhCnTasks,
    player: zhCnPlayer,
    todos: zhCnTodos,
  },
  en: {
    common: enCommon,
    watchlist: enWatchlist,
    accounts: enAccounts,
    auth: enAuth,
    errors: enErrors,
    animation: enAnimation,
    files: enFiles,
    metadataReview: enMetadataReview,
    feeds: enFeeds,
    incidents: enIncidents,
    library: enLibrary,
    season: enSeason,
    settings: {
      system: {
        reauthenticatePrompt:
          "Enter your account password to confirm this sensitive action",
      },
    },
    tasks: enTasks,
    player: enPlayer,
    todos: enTodos,
  },
  ja: {
    common: jaCommon,
    watchlist: jaWatchlist,
    accounts: jaAccounts,
    auth: jaAuth,
    errors: jaErrors,
    animation: jaAnimation,
    files: jaFiles,
    metadataReview: jaMetadataReview,
    feeds: jaFeeds,
    incidents: jaIncidents,
    library: jaLibrary,
    season: jaSeason,
    settings: {
      system: {
        reauthenticatePrompt:
          "この重要な操作を確認するため、アカウントのパスワードを入力してください",
      },
    },
    tasks: jaTasks,
    player: jaPlayer,
    todos: jaTodos,
  },
} as const;
