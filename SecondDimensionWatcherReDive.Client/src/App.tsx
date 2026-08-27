import React from "react";
import { createRoot } from "react-dom/client";
import { SWRConfig } from "swr";

import { Main } from "./Main";
import fetcher from "./auth/httpClient";
import { ToastProvider } from "./components/ToastProvider";
import i18n from "./i18n";
import { setDayjsLocale } from "./utils/initDayjs";

import "./styles.css";

setDayjsLocale(i18n.language);
document.documentElement.lang = i18n.language;
i18n.on("languageChanged", (lng) => {
  setDayjsLocale(lng);
  document.documentElement.lang = lng;
});

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
