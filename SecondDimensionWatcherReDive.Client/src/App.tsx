import React from "react";
import { createRoot } from "react-dom/client";
import { SWRConfig } from "swr";

import { Main } from "./Main";
import fetcher from "./auth/httpClient";
import { DialogHost } from "./components/DialogHost";
import { ToastProvider } from "./components/ToastProvider";
import i18n from "./i18n";
import { refreshWebPushServiceWorker } from "./notifications/webPush";
import "./styles.css";
import { ThemeProvider } from "./theme/ThemeProvider";
import { setDayjsLocale } from "./utils/initDayjs";

setDayjsLocale(i18n.language);
document.documentElement.lang = i18n.language;
i18n.on("languageChanged", (lng) => {
  setDayjsLocale(lng);
  document.documentElement.lang = lng;
});
void refreshWebPushServiceWorker().catch(() => undefined);

const root = createRoot(document.getElementById("app")!);
root.render(
  <React.StrictMode>
    <ThemeProvider>
      <SWRConfig value={{ fetcher: fetcher }}>
        <ToastProvider>
          <Main />
          <DialogHost />
        </ToastProvider>
      </SWRConfig>
    </ThemeProvider>
  </React.StrictMode>,
);
