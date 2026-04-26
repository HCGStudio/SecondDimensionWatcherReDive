import React from "react";
import { useTranslation } from "react-i18next";
import { createBrowserRouter } from "react-router";
import { RouterProvider } from "react-router/dom";

import { ProtectedRoute } from "./components/ProtectedRoute";
import { ChatPage } from "./pages/ChatPage";
import { DownloadedPage } from "./pages/DownloadedPage";
import { DownloadingPage } from "./pages/DownloadingPage";
import { ErrorPage } from "./pages/ErrorPage";
import { FeedsPage } from "./pages/FeedsPage";
import { LoginPage } from "./pages/LoginPage";
import { MainPage, EpisodeListPage } from "./pages/MainPage";
import { PlayerPage } from "./pages/PlayerPage";
import { TasksPage } from "./pages/TasksPage";

const router = createBrowserRouter([
  {
    path: "/",
    element: <ProtectedRoute><MainPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/main",
    element: <ProtectedRoute><MainPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/anime/:tmdbId",
    element: <ProtectedRoute><EpisodeListPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/downloading",
    element: <ProtectedRoute><DownloadingPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/downloaded",
    element: <ProtectedRoute><DownloadedPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/play/:animationId",
    element: <ProtectedRoute><PlayerPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/feeds",
    element: <ProtectedRoute><FeedsPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/tasks",
    element: <ProtectedRoute><TasksPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/chat",
    element: <ProtectedRoute><ChatPage /></ProtectedRoute>,
    errorElement: <ErrorPage />,
  },
  {
    path: "/chat/:conversationId",
    element: <ProtectedRoute><ChatPage /></ProtectedRoute>,
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
  return <RouterProvider router={router} />;
};
