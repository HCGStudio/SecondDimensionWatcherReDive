import React from "react";
import { RouterProvider, createBrowserRouter } from "react-router-dom";

import { DownloadedPage } from "./pages/DownloadedPage";
import { DownloadingPage } from "./pages/DownloadingPage";
import { ErrorPage } from "./pages/ErrorPage";
import { LoginPage } from "./pages/LoginPage";
import { MainPage } from "./pages/MainPage";

const router = createBrowserRouter([
  {
    path: "/",
    element: <MainPage />,
    errorElement: <ErrorPage />,
  },
  {
    path: "/main",
    element: <MainPage />,
    errorElement: <ErrorPage />,
  },
  {
    path: "/downloading",
    element: <DownloadingPage />,
    errorElement: <ErrorPage />,
  },
  {
    path: "/downloaded",
    element: <DownloadedPage />,
    errorElement: <ErrorPage />,
  },
  {
    path: "/login",
    element: <LoginPage />,
    errorElement: <ErrorPage />,
  },
]);

export const Main: React.FC = () => {
  return <RouterProvider router={router} />;
};
