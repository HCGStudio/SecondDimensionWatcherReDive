import {
  Clapperboard,
  Download,
  Home,
  LayoutGrid,
  List,
  Settings,
  User,
} from "lucide-react";
import React from "react";
import { useNavigate, useLocation } from "react-router-dom";

import { useLoginStatus } from "../auth/hooks";
import { cn } from "../lib/cn";

export interface IPageTemplateProps extends React.PropsWithChildren {}

interface NavLinkProps {
  icon: React.ReactNode;
  label: string;
  path: string;
}

const NavLink: React.FC<NavLinkProps> = ({ icon, label, path }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const isActive = location.pathname === path || (path === "/" && location.pathname === "/main");

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

export const PageTemplate: React.FC<IPageTemplateProps> = ({ children }) => {
  const { data: status } = useLoginStatus();
  const navigate = useNavigate();

  return (
    <div className="min-h-screen bg-canvas">
      <header className="sticky top-0 z-30 border-b border-border bg-surface/95 backdrop-blur">
        <nav className="mx-auto flex h-14 max-w-5xl items-center justify-between px-6">
          <div className="flex items-center gap-6">
            <a
              className="flex items-center gap-2 font-serif text-lg font-medium text-foreground cursor-pointer"
              onClick={() => navigate("/")}
            >
              <Clapperboard size={20} />
              二次元观测器
            </a>
            <div className="flex items-center gap-1">
              <NavLink icon={<Home size={16} />} label="主页" path="/" />
              <NavLink icon={<Download size={16} />} label="下载列表" path="/downloading" />
              <NavLink icon={<List size={16} />} label="已下载" path="/downloaded" />
              <NavLink icon={<LayoutGrid size={16} />} label="订阅管理" path="/feeds" />
              <NavLink icon={<Settings size={16} />} label="后台任务" path="/tasks" />
            </div>
          </div>
          <div>
            {status ? (
              <button
                className="text-sm text-muted hover:text-foreground transition-colors"
                onClick={() => {
                  localStorage.clear();
                  location.reload();
                }}
              >
                注销
              </button>
            ) : (
              <button
                className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-foreground transition-colors"
                onClick={() => navigate("/login")}
              >
                <User size={16} />
                登录
              </button>
            )}
          </div>
        </nav>
      </header>
      <main className="mx-auto max-w-5xl px-6 py-8">{children}</main>
    </div>
  );
};
