import React from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useNavigate } from "react-router";

import {
  BellRing,
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
  Search,
  Settings,
  User,
} from "lucide-react";

import { IAuthState, UserRole } from "../auth/IAuthResult";
import { useLoginStatus } from "../auth/hooks";
import { logout, switchProfile } from "../auth/utils";
import i18n, {
  type SupportedLanguage,
  languageLabels,
  supportedLanguages,
} from "../i18n";
import { useIncidents } from "../incidents/hooks";
import { cn } from "../lib/cn";
import { useTodos } from "../todos/hooks";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/DropdownMenu";

interface NavItem {
  icon: React.ReactNode;
  labelKey: string;
  path: string;
  badge?: number;
  administratorOnly?: boolean;
  hiddenForViewer?: boolean;
}

const createNavItems = (
  role?: UserRole,
  incidentCount?: number,
  todoCount?: number,
): NavItem[] =>
  [
    { icon: <Home size={16} />, labelKey: "nav.home", path: "/" },
    { icon: <Search size={16} />, labelKey: "nav.search", path: "/search" },
    {
      icon: <BellRing size={16} />,
      labelKey: "nav.todo",
      path: "/todo",
      badge: todoCount,
      administratorOnly: true,
    },
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
      administratorOnly: true,
    },
    {
      icon: <Settings size={16} />,
      labelKey: "nav.tasks",
      path: "/tasks",
      administratorOnly: true,
    },
    {
      icon: <FileSearch size={16} />,
      labelKey: "nav.metadataReview",
      path: "/metadata-review",
      administratorOnly: true,
    },
    {
      icon: <MessageSquare size={16} />,
      labelKey: "nav.chat",
      path: "/chat",
      hiddenForViewer: true,
    },
    {
      icon: <Cog size={16} />,
      labelKey: "nav.settings",
      path: "/settings",
      administratorOnly: true,
    },
  ].filter(
    (item) =>
      (!item.administratorOnly || role === "Admin") &&
      (!item.hiddenForViewer || role !== "Viewer"),
  );

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
              aria-current={isActive ? "page" : undefined}
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

const UserMenu: React.FC<{ status: IAuthState }> = ({ status }) => {
  const { t, i18n: i18nInstance } = useTranslation();
  const navigate = useNavigate();
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

  const activeProfile = status.profiles.find(
    (profile) => profile.id === status.profileId,
  );

  const onLogout = async () => {
    await logout();
    navigate("/login", { replace: true });
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-sm text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
          aria-label={t("user.account")}
        >
          <User size={16} />
          <span className="hidden sm:inline">
            {activeProfile?.name ?? status.username}
          </span>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-[10rem]">
        <div className="px-3 py-1.5 text-xs uppercase tracking-wide text-subtle">
          {status.username} · {status.role}
        </div>
        {status.profiles.map((profile) => (
          <DropdownMenuItem
            key={profile.id}
            onSelect={() => {
              if (profile.id === status.profileId) return;
              const pin = profile.hasPin
                ? window.prompt(t("user.profilePin"))
                : undefined;
              if (profile.hasPin && pin === null) return;
              void switchProfile(profile.id, pin || undefined).then(() => {
                window.location.assign("/");
              });
            }}
          >
            <Check
              size={14}
              className={
                profile.id === status.profileId ? "opacity-100" : "opacity-0"
              }
            />
            {profile.name}
          </DropdownMenuItem>
        ))}
        <DropdownMenuItem onSelect={() => navigate("/account")}>
          <User size={14} />
          {t("user.manageAccount")}
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <div className="px-3 py-1.5 text-xs uppercase tracking-wide text-subtle">
          {t("user.language")}
        </div>
        <DropdownMenuRadioGroup
          value={currentLng}
          onValueChange={(lng) => {
            void i18n.changeLanguage(lng);
          }}
        >
          {supportedLanguages.map((lng) => (
            <DropdownMenuRadioItem key={lng} value={lng}>
              <Check
                size={14}
                className={lng === currentLng ? "opacity-100" : "opacity-0"}
              />
              {languageLabels[lng]}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem color="danger" onSelect={() => void onLogout()}>
          {t("user.logout")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
};

export const AppHeader: React.FC = () => {
  const { t } = useTranslation();
  const { data: status } = useLoginStatus();
  const { data: incidents } = useIncidents({
    take: 1,
    enabled: status?.role === "Admin",
  });
  const { data: todos } = useTodos({
    take: 1,
    enabled: status?.role === "Admin",
  });
  const navigate = useNavigate();
  const items = createNavItems(
    status?.role,
    incidents?.openCount,
    todos?.unreadCount,
  );
  const location = useLocation();
  const [searchQuery, setSearchQuery] = React.useState(
    location.pathname === "/search"
      ? (new URLSearchParams(location.search).get("q") ?? "")
      : "",
  );

  React.useEffect(() => {
    if (location.pathname === "/search") {
      setSearchQuery(new URLSearchParams(location.search).get("q") ?? "");
    }
  }, [location.pathname, location.search]);
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
        <div className="ml-auto flex min-w-0 items-center gap-2">
          {status ? (
            <form
              className="hidden w-44 sm:block 2xl:w-56"
              onSubmit={(event) => {
                event.preventDefault();
                const query = searchQuery.trim();
                navigate(
                  query ? `/search?q=${encodeURIComponent(query)}` : "/search",
                );
              }}
            >
              <label className="relative block">
                <span className="sr-only">{t("nav.search")}</span>
                <Search
                  size={15}
                  className="absolute left-2.5 top-2 text-subtle"
                />
                <input
                  value={searchQuery}
                  onChange={(event) => setSearchQuery(event.target.value)}
                  placeholder={t("nav.searchPlaceholder")}
                  className="w-full rounded-md border border-border bg-canvas py-1.5 pl-8 pr-2 text-sm text-foreground outline-hidden placeholder:text-subtle focus:border-focus focus:ring-2 focus:ring-focus"
                />
              </label>
            </form>
          ) : null}
          {status ? (
            <UserMenu status={status} />
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
