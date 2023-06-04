import React from "react";
import { RouterProvider, createBrowserRouter } from "react-router-dom";

import { LoginPage } from "./pages/LoginPage";
import { MainPage } from "./pages/MainPage";

const router = createBrowserRouter([
  {
    path: "/",
    element: <MainPage />,
    errorElement: <MainPage />,
  },
  {
    path: "/main",
    element: <MainPage />,
    errorElement: <MainPage />,
  },
  {
    path: "/login",
    element: <LoginPage />,
  },
]);

export const Main: React.FC = () => {
  return <RouterProvider router={router} />;
};
