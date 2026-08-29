import React from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useNavigate } from "react-router";

import {
  Check,
  Clapperboard,
  Cog,
  Download,
  FileSearch,
  FolderOpen,
  Home,
  Inbox,
  LayoutGrid,
  List,
  Menu,
  MessageSquare,
  Settings,
  User,
} from "lucide-react";

import { useLoginStatus } from "../auth/hooks";
import i18n, {
  type SupportedLanguage,
  languageLabels,
  supportedLanguages,
} from "../i18n";
import { useIncidents } from "../incidents/hooks";
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
  badge?: number;
}

const createNavItems = (incidentCount?: number): NavItem[] => [
  { icon: <Home size={16} />, labelKey: "nav.home", path: "/" },
  {
    icon: <Download size={16} />,
    labelKey: "nav.downloading",
    path: "/downloading",
  },
  {
    icon: <List size={16} />,
    labelKey: "nav.downloaded",
    path: "/downloaded",
  },
  { icon: <FolderOpen size={16} />, labelKey: "nav.files", path: "/files" },
  { icon: <LayoutGrid size={16} />, labelKey: "nav.feeds", path: "/feeds" },
  {
    icon: <Inbox size={16} />,
    labelKey: "nav.incidents",
    path: "/incidents",
    badge: incidentCount,
  },
  { icon: <Settings size={16} />, labelKey: "nav.tasks", path: "/tasks" },
  {
    icon: <FileSearch size={16} />,
    labelKey: "nav.metadataReview",
    path: "/metadata-review",
  },
  { icon: <MessageSquare size={16} />, labelKey: "nav.chat", path: "/chat" },
  { icon: <Cog size={16} />, labelKey: "nav.settings", path: "/settings" },
];

const isPathActive = (pathname: string, path: string): boolean =>
  pathname === path || (path === "/" && pathname === "/main");

interface NavLinkProps {
  icon: React.ReactNode;
  label: string;
  path: string;
  badge?: number;
}

const NavLink: React.FC<NavLinkProps> = ({ icon, label, path, badge }) => {
  const location = useLocation();
  const isActive = isPathActive(location.pathname, path);

  return (
    <Link
      to={path}
      aria-current={isActive ? "page" : undefined}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus",
        isActive
          ? "bg-canvas text-foreground"
          : "text-muted hover:text-foreground hover:bg-canvas",
      )}
    >
      {icon}
      {label}
      {badge != null && badge > 0 ? (
        <span className="min-w-4 rounded-full bg-error px-1 text-center text-[10px] leading-4 text-surface">
          {badge > 99 ? "99+" : badge}
        </span>
      ) : null}
    </Link>
  );
};

const MobileNavMenu: React.FC<{ items: NavItem[] }> = ({ items }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="inline-flex items-center justify-center rounded-md p-1.5 text-muted transition-colors hover:bg-canvas hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus xl:hidden"
          aria-label={t("nav.menu")}
        >
          <Menu size={18} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="start"
        sideOffset={8}
        className="min-w-[12rem]"
      >
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
              {item.badge != null && item.badge > 0 ? (
                <span className="ml-auto min-w-5 rounded-full bg-error px-1.5 text-center text-[10px] leading-5 text-surface">
                  {item.badge > 99 ? "99+" : item.badge}
                </span>
              ) : null}
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
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-sm text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
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
  const { data: incidents } = useIncidents({ take: 1 });
  const navigate = useNavigate();
  const items = createNavItems(incidents?.openCount);

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur">
      <nav className="flex h-14 items-center justify-between gap-2 px-4 sm:px-6">
        <div className="flex min-w-0 items-center gap-2 xl:gap-4">
          <MobileNavMenu items={items} />
          <Link
            to="/"
            className="flex min-w-0 items-center gap-2 rounded-md font-serif text-lg font-medium text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
          >
            <Clapperboard size={20} className="shrink-0" />
            <span className="truncate">{t("appName")}</span>
          </Link>
          <div className="hidden xl:flex items-center gap-0.5">
            {items.map((item) => (
              <NavLink
                key={item.path}
                icon={item.icon}
                label={t(item.labelKey)}
                path={item.path}
                badge={item.badge}
              />
            ))}
          </div>
        </div>
        <div className="shrink-0">
          {status ? (
            <UserMenu />
          ) : (
            <button
              type="button"
              className="inline-flex items-center gap-1.5 rounded-md text-sm text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
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
