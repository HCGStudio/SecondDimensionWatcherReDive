import React from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useNavigate } from "react-router";

import {
  BellRing,
  Check,
  ChevronDown,
  ChevronRight,
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
import { BrandIcon } from "./BrandIcon";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/DropdownMenu";

type NavGroup = "library" | "watching" | "management";
const navGroups: NavGroup[] = ["library", "watching", "management"];

interface NavItem {
  icon: React.ReactNode;
  labelKey: string;
  path: string;
  group: NavGroup;
  badge?: number;
  administratorOnly?: boolean;
  hiddenForViewer?: boolean;
}

const createNavItems = (
  role?: UserRole,
  incidentCount?: number,
  todoCount?: number,
): NavItem[] => {
  const items: NavItem[] = [
    {
      icon: <Home size={17} />,
      labelKey: "nav.home",
      path: "/",
      group: "library",
    },
    {
      icon: <Search size={17} />,
      labelKey: "nav.search",
      path: "/search",
      group: "library",
    },
    {
      icon: <BellRing size={17} />,
      labelKey: "nav.todo",
      path: "/todo",
      group: "management",
      badge: todoCount,
      administratorOnly: true,
    },
    {
      icon: <Download size={17} />,
      labelKey: "nav.downloading",
      path: "/downloading",
      group: "watching",
    },
    {
      icon: <List size={17} />,
      labelKey: "nav.downloaded",
      path: "/downloaded",
      group: "library",
    },
    {
      icon: <FolderOpen size={17} />,
      labelKey: "nav.files",
      path: "/files",
      group: "library",
    },
    {
      icon: <LayoutGrid size={17} />,
      labelKey: "nav.feeds",
      path: "/feeds",
      group: "watching",
    },
    {
      icon: <Inbox size={17} />,
      labelKey: "nav.incidents",
      path: "/incidents",
      group: "management",
      badge: incidentCount,
      administratorOnly: true,
    },
    {
      icon: <Settings size={17} />,
      labelKey: "nav.tasks",
      path: "/tasks",
      group: "management",
      administratorOnly: true,
    },
    {
      icon: <FileSearch size={17} />,
      labelKey: "nav.metadataReview",
      path: "/metadata-review",
      group: "management",
      administratorOnly: true,
    },
    {
      icon: <MessageSquare size={17} />,
      labelKey: "nav.chat",
      path: "/chat",
      group: "watching",
      hiddenForViewer: true,
    },
    {
      icon: <Cog size={17} />,
      labelKey: "nav.settings",
      path: "/settings",
      group: "management",
      administratorOnly: true,
    },
  ];
  return items.filter(
    (item) =>
      (!item.administratorOnly || role === "Admin") &&
      (!item.hiddenForViewer || role !== "Viewer"),
  );
};

const isPathActive = (pathname: string, path: string): boolean =>
  pathname === path ||
  (path !== "/" && pathname.startsWith(`${path}/`)) ||
  (path === "/" && (pathname === "/main" || pathname.startsWith("/anime/")));

const NavBadge: React.FC<{ count?: number }> = ({ count }) =>
  count != null && count > 0 ? (
    <span className="ml-auto min-w-5 shrink-0 rounded-md bg-accent/10 px-1.5 text-center text-[10px] font-semibold leading-5 text-accent tabular-nums">
      {count > 99 ? "99+" : count}
    </span>
  ) : null;

const NavLink: React.FC<{ item: NavItem }> = ({ item }) => {
  const { t } = useTranslation();
  const location = useLocation();
  const isActive = isPathActive(location.pathname, item.path);

  return (
    <Link
      to={item.path}
      aria-current={isActive ? "page" : undefined}
      className={cn(
        "flex min-w-0 items-center gap-3 rounded-lg px-3 py-2 text-[13px] font-medium transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus",
        isActive
          ? "bg-accent/10 text-accent"
          : "text-muted hover:bg-tint hover:text-foreground",
      )}
    >
      <span aria-hidden="true" className="shrink-0">
        {item.icon}
      </span>
      <span className="min-w-0 leading-5">{t(item.labelKey)}</span>
      <NavBadge count={item.badge} />
    </Link>
  );
};

const Brand: React.FC = () => {
  const { t } = useTranslation();
  return (
    <Link
      to="/"
      className="flex min-w-0 items-center gap-2.5 rounded-lg focus:outline-hidden focus:ring-2 focus:ring-focus"
    >
      <BrandIcon className="h-9 w-9 shrink-0 text-accent" />
      <div className="min-w-0 leading-tight">
        <span className="block text-[10px] font-bold tracking-[0.08em] text-foreground">
          SECOND DIMENSION
        </span>
        <span className="mt-1 block text-[11px] text-muted">
          {t("appName")}
        </span>
      </div>
    </Link>
  );
};

const DesktopSidebar: React.FC<{ items: NavItem[] }> = ({ items }) => {
  const { t } = useTranslation();

  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden h-dvh w-[216px] flex-col border-r border-border bg-surface px-4 lg:flex">
      <div className="flex min-h-20 shrink-0 items-center px-2">
        <Brand />
      </div>
      <nav
        aria-label={t("nav.navigation")}
        className="min-h-0 flex-1 space-y-3 overflow-y-auto py-3"
      >
        {navGroups.map((group) => {
          const groupItems = items.filter((item) => item.group === group);
          if (groupItems.length === 0) return null;
          return (
            <div key={group}>
              <p className="mb-1 px-3 text-[10px] font-medium uppercase tracking-[0.14em] text-subtle">
                {t(`nav.groups.${group}`)}
              </p>
              <div className="space-y-0.5">
                {groupItems.map((item) => (
                  <NavLink key={item.path} item={item} />
                ))}
              </div>
            </div>
          );
        })}
      </nav>
      <div className="shrink-0 border-t border-border px-3 py-4">
        <p className="text-xs font-medium text-foreground">
          {t("nav.yourSpace")}
        </p>
        <p className="mt-1 text-[10px] tracking-wide text-subtle">
          SDW Re:Dive
        </p>
      </div>
    </aside>
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
          className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-lg text-muted transition-colors hover:bg-tint hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus lg:hidden"
          aria-label={t("nav.menu")}
        >
          <Menu size={20} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="start"
        sideOffset={10}
        className="max-h-[var(--radix-dropdown-menu-content-available-height)] w-64 max-w-[calc(100vw-2rem)] overflow-y-auto p-2"
      >
        {navGroups.map((group) => {
          const groupItems = items.filter((item) => item.group === group);
          if (groupItems.length === 0) return null;
          return (
            <React.Fragment key={group}>
              <div className="px-3 pb-2 pt-3 text-[10px] font-medium uppercase tracking-widest text-subtle">
                {t(`nav.groups.${group}`)}
              </div>
              {groupItems.map((item) => {
                const isActive = isPathActive(location.pathname, item.path);
                return (
                  <DropdownMenuItem
                    key={item.path}
                    aria-current={isActive ? "page" : undefined}
                    onSelect={() => navigate(item.path)}
                    className={cn(
                      "min-h-11 gap-3 rounded-lg",
                      isActive && "bg-accent/10 font-medium text-accent",
                    )}
                  >
                    <span aria-hidden="true" className="shrink-0">
                      {item.icon}
                    </span>
                    {t(item.labelKey)}
                    <NavBadge count={item.badge} />
                  </DropdownMenuItem>
                );
              })}
            </React.Fragment>
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
  const displayName = activeProfile?.name ?? status.username;

  const onLogout = async () => {
    await logout();
    navigate("/login", { replace: true });
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="inline-flex min-h-10 shrink-0 items-center gap-2 rounded-lg px-1 text-sm text-muted transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus sm:px-2"
          aria-label={t("user.account")}
        >
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent/10 text-xs font-semibold text-accent">
            {Array.from(displayName)[0]?.toUpperCase() ?? <User size={15} />}
          </span>
          <span className="hidden max-w-32 truncate text-xs font-medium sm:inline">
            {displayName}
          </span>
          <ChevronDown size={13} className="hidden text-subtle sm:block" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="max-h-[var(--radix-dropdown-menu-content-available-height)] min-w-56 max-w-[calc(100vw-2rem)] overflow-y-auto p-1.5"
      >
        <div className="break-words px-3 py-2 text-xs text-subtle">
          {status.username} · {t(`user.roles.${status.role}`)}
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
            className="min-h-10 rounded-md"
          >
            <Check
              size={14}
              className={cn(
                "shrink-0",
                profile.id === status.profileId ? "opacity-100" : "opacity-0",
              )}
            />
            <span className="min-w-0 break-words">{profile.name}</span>
          </DropdownMenuItem>
        ))}
        <DropdownMenuItem
          onSelect={() => navigate("/account")}
          className="min-h-10 rounded-md"
        >
          <User size={14} />
          {t("user.manageAccount")}
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <div className="px-3 py-2 text-[10px] uppercase tracking-widest text-subtle">
          {t("user.language")}
        </div>
        <DropdownMenuRadioGroup
          value={currentLng}
          onValueChange={(lng) => {
            void i18n.changeLanguage(lng);
          }}
        >
          {supportedLanguages.map((lng) => (
            <DropdownMenuRadioItem
              key={lng}
              value={lng}
              className="min-h-10 rounded-md"
            >
              <Check
                size={14}
                className={lng === currentLng ? "opacity-100" : "opacity-0"}
              />
              {languageLabels[lng]}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          color="danger"
          onSelect={() => void onLogout()}
          className="min-h-10 rounded-md"
        >
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
  const currentItem = items.find((item) =>
    isPathActive(location.pathname, item.path),
  );
  const pageTitle = location.pathname.startsWith("/anime/")
    ? t("nav.episodes")
    : location.pathname.startsWith("/play/")
      ? t("nav.player")
      : location.pathname === "/account"
        ? t("user.manageAccount")
        : location.pathname === "/login"
          ? t("user.login")
          : currentItem
            ? t(currentItem.labelKey)
            : t("appName");

  React.useEffect(() => {
    if (location.pathname === "/search") {
      setSearchQuery(new URLSearchParams(location.search).get("q") ?? "");
    }
  }, [location.pathname, location.search]);

  return (
    <>
      <DesktopSidebar items={items} />
      <header className="sticky top-0 z-30 shrink-0 border-b border-border bg-surface/95 backdrop-blur lg:ml-[216px]">
        <div className="mx-auto flex h-16 max-w-[1600px] items-center justify-between gap-2 px-4 sm:px-7 lg:h-20 lg:px-10">
          <div className="flex min-w-0 items-center gap-2">
            <MobileNavMenu items={items} />
            <Link
              to="/"
              aria-label={t("appName")}
              className="shrink-0 rounded-lg focus:outline-hidden focus:ring-2 focus:ring-focus lg:hidden"
            >
              <BrandIcon className="h-7 w-7 text-accent" />
            </Link>
            <div className="flex min-w-0 items-center gap-3 text-xs">
              <Link
                to="/"
                className="hidden text-muted hover:text-accent lg:block"
              >
                {t("appName")}
              </Link>
              <ChevronRight size={13} className="hidden text-subtle lg:block" />
              <span className="truncate font-medium text-foreground">
                {pageTitle}
              </span>
            </div>
          </div>
          <div className="ml-auto flex shrink-0 items-center gap-2 sm:gap-4">
            {status ? (
              <>
                <form
                  role="search"
                  className="hidden w-48 sm:block xl:w-60"
                  onSubmit={(event) => {
                    event.preventDefault();
                    const query = searchQuery.trim();
                    navigate(
                      query
                        ? `/search?q=${encodeURIComponent(query)}`
                        : "/search",
                    );
                  }}
                >
                  <label className="relative block">
                    <span className="sr-only">{t("nav.search")}</span>
                    <Search
                      size={15}
                      className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-subtle"
                    />
                    <input
                      type="search"
                      value={searchQuery}
                      onChange={(event) => setSearchQuery(event.target.value)}
                      placeholder={t("nav.searchPlaceholder")}
                      className="h-10 w-full rounded-lg border border-border bg-surface-muted pl-9 pr-3 text-xs text-foreground outline-hidden placeholder:text-subtle focus:border-focus focus:ring-2 focus:ring-focus"
                    />
                  </label>
                </form>
                <Link
                  to="/search"
                  aria-label={t("nav.search")}
                  className="flex h-10 w-10 items-center justify-center rounded-lg text-muted hover:bg-tint hover:text-accent focus:outline-hidden focus:ring-2 focus:ring-focus sm:hidden"
                >
                  <Search size={18} />
                </Link>
              </>
            ) : null}
            {status ? (
              <UserMenu status={status} />
            ) : (
              <button
                type="button"
                className="inline-flex h-10 items-center gap-2 rounded-lg px-2 text-xs font-medium text-muted transition-colors hover:text-accent focus:outline-hidden focus:ring-2 focus:ring-focus"
                onClick={() => navigate("/login")}
              >
                <User size={16} />
                {t("user.login")}
              </button>
            )}
          </div>
        </div>
      </header>
    </>
  );
};
