import {
  Check,
  Clapperboard,
  Download,
  Home,
  LayoutGrid,
  List,
  Menu,
  MessageSquare,
  Settings,
  User,
} from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useLocation } from "react-router";

import { useLoginStatus } from "../auth/hooks";
import i18n, {
  languageLabels,
  supportedLanguages,
  type SupportedLanguage,
} from "../i18n";
import { cn } from "../lib/cn";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/DropdownMenu";

interface NavItem {
  icon: React.ReactNode;
  labelKey: string;
  path: string;
}

const useNavItems = (): NavItem[] => [
  { icon: <Home size={16} />, labelKey: "nav.home", path: "/" },
  { icon: <Download size={16} />, labelKey: "nav.downloading", path: "/downloading" },
  { icon: <List size={16} />, labelKey: "nav.downloaded", path: "/downloaded" },
  { icon: <LayoutGrid size={16} />, labelKey: "nav.feeds", path: "/feeds" },
  { icon: <Settings size={16} />, labelKey: "nav.tasks", path: "/tasks" },
  { icon: <MessageSquare size={16} />, labelKey: "nav.chat", path: "/chat" },
];

const isPathActive = (pathname: string, path: string): boolean =>
  pathname === path || (path === "/" && pathname === "/main");

interface NavLinkProps {
  icon: React.ReactNode;
  label: string;
  path: string;
}

const NavLink: React.FC<NavLinkProps> = ({ icon, label, path }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const isActive = isPathActive(location.pathname, path);

  return (
    <button
      onClick={() => navigate(path)}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
        isActive
          ? "bg-canvas text-foreground"
          : "text-muted hover:text-foreground hover:bg-canvas",
      )}
    >
      {icon}
      {label}
    </button>
  );
};

const MobileNavMenu: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const items = useNavItems();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="lg:hidden inline-flex items-center justify-center rounded-md p-1.5 text-muted hover:text-foreground hover:bg-canvas transition-colors"
          aria-label={t("nav.menu")}
        >
          <Menu size={18} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" sideOffset={8} className="min-w-[12rem]">
        {items.map((item) => {
          const isActive = isPathActive(location.pathname, item.path);
          return (
            <DropdownMenuItem
              key={item.path}
              onSelect={() => navigate(item.path)}
              className={cn(
                "gap-2.5",
                isActive && "bg-canvas text-foreground font-medium",
              )}
            >
              <span
                className={cn(
                  "inline-flex",
                  isActive ? "text-foreground" : "text-muted",
                )}
              >
                {item.icon}
              </span>
              {t(item.labelKey)}
            </DropdownMenuItem>
          );
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  );
};

const UserMenu: React.FC = () => {
  const { t, i18n: i18nInstance } = useTranslation();
  const resolved = (
    i18nInstance.resolvedLanguage ??
    i18nInstance.language ??
    "zh-cn"
  ).toLowerCase();
  const currentLng: SupportedLanguage = supportedLanguages.includes(
    resolved as SupportedLanguage,
  )
    ? (resolved as SupportedLanguage)
    : "zh-cn";

  const onLogout = () => {
    localStorage.removeItem("auth");
    location.reload();
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-sm text-muted hover:text-foreground transition-colors"
          aria-label={t("user.account")}
        >
          <User size={16} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-[10rem]">
        <div className="px-3 py-1.5 text-xs uppercase tracking-wide text-subtle">
          {t("user.language")}
        </div>
        {supportedLanguages.map((lng) => (
          <DropdownMenuItem
            key={lng}
            onSelect={() => {
              void i18n.changeLanguage(lng);
            }}
          >
            <Check
              size={14}
              className={lng === currentLng ? "opacity-100" : "opacity-0"}
            />
            {languageLabels[lng]}
          </DropdownMenuItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem color="danger" onSelect={onLogout}>
          {t("user.logout")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
};

export const AppHeader: React.FC = () => {
  const { t } = useTranslation();
  const { data: status } = useLoginStatus();
  const navigate = useNavigate();
  const items = useNavItems();

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur">
      <nav className="flex h-14 items-center justify-between gap-2 px-4 sm:px-6">
        <div className="flex min-w-0 items-center gap-2 lg:gap-6">
          <MobileNavMenu />
          <a
            className="flex min-w-0 items-center gap-2 font-serif text-lg font-medium text-foreground cursor-pointer"
            onClick={() => navigate("/")}
          >
            <Clapperboard size={20} className="shrink-0" />
            <span className="truncate">{t("appName")}</span>
          </a>
          <div className="hidden lg:flex items-center gap-1">
            {items.map((item) => (
              <NavLink
                key={item.path}
                icon={item.icon}
                label={t(item.labelKey)}
                path={item.path}
              />
            ))}
          </div>
        </div>
        <div className="shrink-0">
          {status ? (
            <UserMenu />
          ) : (
            <button
              className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-foreground transition-colors"
              onClick={() => navigate("/login")}
            >
              <User size={16} />
              {t("user.login")}
            </button>
          )}
        </div>
      </nav>
    </header>
  );
};
