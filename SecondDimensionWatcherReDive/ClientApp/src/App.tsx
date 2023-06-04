import {
  EuiProvider,
  EuiThemeModifications,
  EuiThemeProvider,
} from "@elastic/eui";
import React from "react";
import { createRoot } from "react-dom/client";
import { SWRConfig } from "swr";

import { icon as accessibility } from "@elastic/eui/es/components/icon/assets/accessibility";
import { icon as apps } from "@elastic/eui/es/components/icon/assets/apps";
import { icon as arrowEnd } from "@elastic/eui/es/components/icon/assets/arrowEnd";
import { icon as arrowStart } from "@elastic/eui/es/components/icon/assets/arrowStart";
import { icon as arrowDown } from "@elastic/eui/es/components/icon/assets/arrow_down";
import { icon as arrowLeft } from "@elastic/eui/es/components/icon/assets/arrow_left";
import { icon as arrowRight } from "@elastic/eui/es/components/icon/assets/arrow_right";
import { icon as arrowUp } from "@elastic/eui/es/components/icon/assets/arrow_up";
import { icon as clock } from "@elastic/eui/es/components/icon/assets/clock";
import { icon as copyClipboard } from "@elastic/eui/es/components/icon/assets/copy_clipboard";
import { icon as cross } from "@elastic/eui/es/components/icon/assets/cross";
import { icon as doubleArrowRight } from "@elastic/eui/es/components/icon/assets/doubleArrowRight";
import { icon as download } from "@elastic/eui/es/components/icon/assets/download";
import { icon as eye } from "@elastic/eui/es/components/icon/assets/eye";
import { icon as eyeClosed } from "@elastic/eui/es/components/icon/assets/eye_closed";
import { icon as home } from "@elastic/eui/es/components/icon/assets/home";
import { icon as kubernetesPod } from "@elastic/eui/es/components/icon/assets/kubernetesPod";
import { icon as list } from "@elastic/eui/es/components/icon/assets/list";
import { icon as lock } from "@elastic/eui/es/components/icon/assets/lock";
import { icon as sortDown } from "@elastic/eui/es/components/icon/assets/sort_down";
import { icon as user } from "@elastic/eui/es/components/icon/assets/user";
import { icon as videoPlayer } from "@elastic/eui/es/components/icon/assets/videoPlayer";
import { icon as warning } from "@elastic/eui/es/components/icon/assets/warning";
import { appendIconComponentCache } from "@elastic/eui/es/components/icon/icon";

import { Main } from "./Main";
import fetcher from "./auth/httpClient";
import { initDayjs } from "./utils/initDayjs";

import "@elastic/eui/dist/eui_theme_light.css";

initDayjs();

appendIconComponentCache({
  accessibility,
  kubernetesPod,
  home,
  download,
  list,
  user,
  cross,
  copyClipboard,
  eye,
  lock,
  eyeClosed,
  warning,
  videoPlayer,
  apps,
  clock,
  doubleArrowRight,
  sortDown,
  arrowLeft,
  arrowRight,
  arrowStart,
  arrowEnd,
  arrowUp,
  arrowDown,
});

// Success from eui is too ugly, overriding color from bootstrap
// See https://getbootstrap.com/docs/5.3/customize/color/
const themeModify: EuiThemeModifications = {
  colors: {
    LIGHT: {
      success: "#198754",
      successText: "#198754",
    },
    DARK: {
      success: "#198754",
      successText: "#198754",
    },
  },
};

const root = createRoot(document.getElementById("app")!);
root.render(
  <React.StrictMode>
    <EuiProvider colorMode="light" modify={themeModify}>
      <EuiThemeProvider>
        <SWRConfig value={{ fetcher: fetcher }}>
          <Main />
        </SWRConfig>
      </EuiThemeProvider>
    </EuiProvider>
  </React.StrictMode>,
);
