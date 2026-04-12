import React from "react";
import { createRoot } from "react-dom/client";
import { SWRConfig } from "swr";

import { Main } from "./Main";
import fetcher from "./auth/httpClient";
import { ToastProvider } from "./components/ToastProvider";
import { initDayjs } from "./utils/initDayjs";

import "./styles.css";

initDayjs();

const root = createRoot(document.getElementById("app")!);
root.render(
  <React.StrictMode>
    <SWRConfig value={{ fetcher: fetcher }}>
      <ToastProvider>
        <Main />
      </ToastProvider>
    </SWRConfig>
  </React.StrictMode>,
);
