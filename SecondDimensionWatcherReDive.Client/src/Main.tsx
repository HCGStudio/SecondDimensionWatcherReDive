import React from "react";
import { useTranslation } from "react-i18next";
import { createBrowserRouter } from "react-router";
import { RouterProvider } from "react-router/dom";

import { ProtectedRoute } from "./components/ProtectedRoute";
import { ErrorPage } from "./pages/ErrorPage";
import { RouteLoadingBoundary } from "./routes/RouteLoadingBoundary";
import {
  loadChatPage,
  loadDownloadedPage,
  loadDownloadingPage,
  loadFeedsPage,
  loadFilesPage,
  loadIncidentsPage,
  loadLoginPage,
  loadMainPage,
  loadMetadataReviewPage,
  loadPlayerPage,
  loadSearchPage,
  loadSettingsPage,
  loadTasksPage,
} from "./routes/pageLoaders";

const ChatPage = React.lazy(async () => ({
  default: (await loadChatPage()).ChatPage,
}));
const DownloadedPage = React.lazy(async () => ({
  default: (await loadDownloadedPage()).DownloadedPage,
}));
const DownloadingPage = React.lazy(async () => ({
  default: (await loadDownloadingPage()).DownloadingPage,
}));
const FeedsPage = React.lazy(async () => ({
  default: (await loadFeedsPage()).FeedsPage,
}));
const FilesPage = React.lazy(async () => ({
  default: (await loadFilesPage()).FilesPage,
}));
const IncidentsPage = React.lazy(async () => ({
  default: (await loadIncidentsPage()).IncidentsPage,
}));
const LoginPage = React.lazy(async () => ({
  default: (await loadLoginPage()).LoginPage,
}));
const MainPage = React.lazy(async () => ({
  default: (await loadMainPage()).MainPage,
}));
const EpisodeListPage = React.lazy(async () => ({
  default: (await loadMainPage()).EpisodeListPage,
}));
const MetadataReviewPage = React.lazy(async () => ({
  default: (await loadMetadataReviewPage()).MetadataReviewPage,
}));
const PlayerPage = React.lazy(async () => ({
  default: (await loadPlayerPage()).PlayerPage,
}));
const SearchPage = React.lazy(async () => ({
  default: (await loadSearchPage()).SearchPage,
}));
const SettingsPage = React.lazy(async () => ({
  default: (await loadSettingsPage()).SettingsPage,
}));
const TasksPage = React.lazy(async () => ({
  default: (await loadTasksPage()).TasksPage,
}));

const router = createBrowserRouter([
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <MainPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/main",
    element: (
      <ProtectedRoute>
        <MainPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/anime/:tmdbId",
    element: (
      <ProtectedRoute>
        <EpisodeListPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/downloading",
    element: (
      <ProtectedRoute>
        <DownloadingPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/downloaded",
    element: (
      <ProtectedRoute>
        <DownloadedPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/files",
    element: (
      <ProtectedRoute>
        <FilesPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/search",
    element: (
      <ProtectedRoute>
        <SearchPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/play/:animationId",
    element: (
      <ProtectedRoute>
        <PlayerPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/incidents",
    element: (
      <ProtectedRoute>
        <IncidentsPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/feeds",
    element: (
      <ProtectedRoute>
        <FeedsPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/tasks",
    element: (
      <ProtectedRoute>
        <TasksPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/metadata-review",
    element: (
      <ProtectedRoute>
        <MetadataReviewPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/chat",
    element: (
      <ProtectedRoute>
        <ChatPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/chat/:conversationId",
    element: (
      <ProtectedRoute>
        <ChatPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/settings",
    element: (
      <ProtectedRoute>
        <SettingsPage />
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: "/login",
    element: <LoginPage />,
    errorElement: <ErrorPage />,
  },
]);

export const Main: React.FC = () => {
  const { t } = useTranslation();
  React.useEffect(() => {
    document.title = `${t("appName")} Re:Dive`;
  }, [t]);
  return (
    <RouteLoadingBoundary>
      <RouterProvider router={router} />
    </RouteLoadingBoundary>
  );
};
