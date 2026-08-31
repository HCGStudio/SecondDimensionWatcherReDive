export const loadMainPage = () => import("../pages/MainPage");
export const loadDownloadingPage = () => import("../pages/DownloadingPage");
export const loadDownloadedPage = () => import("../pages/DownloadedPage");
export const loadFilesPage = () => import("../pages/FilesPage");
export const loadPlayerPage = () => import("../pages/PlayerPage");
export const loadSearchPage = () => import("../pages/SearchPage");
export const loadIncidentsPage = () => import("../pages/IncidentsPage");
export const loadFeedsPage = () => import("../pages/FeedsPage");
export const loadTasksPage = () => import("../pages/TasksPage");
export const loadMetadataReviewPage = () =>
  import("../pages/MetadataReviewPage");
export const loadChatPage = () => import("../pages/ChatPage");
export const loadSettingsPage = () => import("../pages/SettingsPage");
export const loadLoginPage = () => import("../pages/LoginPage");

/**
 * Warms only the lightweight player route. Player media probing, subtitle
 * parsing, and FFmpeg remain separate dynamic imports inside PlayerPage.
 */
export const preloadPlayerPage = (): void => {
  void loadPlayerPage().catch(() => {
    // Navigation owns the retry UI. Preloading is deliberately best-effort.
  });
};
