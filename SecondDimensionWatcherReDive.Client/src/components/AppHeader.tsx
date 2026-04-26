import {
  Check,
  Clapperboard,
  Download,
  Home,
  LayoutGrid,
  List,
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

interface NavLinkProps {
  icon: React.ReactNode;
  label: string;
  path: string;
}

const NavLink: React.FC<NavLinkProps> = ({ icon, label, path }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const isActive =
    location.pathname === path ||
    (path === "/" && location.pathname === "/main");

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

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur">
      <nav className="flex h-14 items-center justify-between px-6">
        <div className="flex items-center gap-6">
          <a
            className="flex items-center gap-2 font-serif text-lg font-medium text-foreground cursor-pointer"
            onClick={() => navigate("/")}
          >
            <Clapperboard size={20} />
            {t("appName")}
          </a>
          <div className="flex items-center gap-1">
            <NavLink icon={<Home size={16} />} label={t("nav.home")} path="/" />
            <NavLink
              icon={<Download size={16} />}
              label={t("nav.downloading")}
              path="/downloading"
            />
            <NavLink
              icon={<List size={16} />}
              label={t("nav.downloaded")}
              path="/downloaded"
            />
            <NavLink
              icon={<LayoutGrid size={16} />}
              label={t("nav.feeds")}
              path="/feeds"
            />
            <NavLink
              icon={<Settings size={16} />}
              label={t("nav.tasks")}
              path="/tasks"
            />
            <NavLink
              icon={<MessageSquare size={16} />}
              label={t("nav.chat")}
              path="/chat"
            />
          </div>
        </div>
        <div>
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
