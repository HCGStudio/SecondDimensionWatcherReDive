import React from "react";
import { useTranslation } from "react-i18next";

import {
  AlertTriangle,
  CheckCircle2,
  PackageCheck,
  Plug,
  ShieldCheck,
  ShieldX,
  Trash2,
  Upload,
} from "lucide-react";

import {
  installPlugin,
  previewPlugin,
  setPluginEnabled,
  uninstallPlugin,
} from "../../plugins/api";
import { usePlugins } from "../../plugins/hooks";
import { PluginCapabilities, PluginPackagePreview } from "../../plugins/types";
import { useToast } from "../ToastProvider";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { Spinner } from "../ui/Spinner";

export const PluginSettingsSection: React.FC = () => {
  const { t } = useTranslation("settings");
  const { addToast } = useToast();
  const { data: plugins, error, mutate } = usePlugins();
  const [file, setFile] = React.useState<File | null>(null);
  const [preview, setPreview] = React.useState<PluginPackagePreview | null>(
    null,
  );
  const [approved, setApproved] = React.useState(false);
  const [busy, setBusy] = React.useState(false);

  const run = React.useCallback(
    async (operation: () => Promise<unknown>, success: string) => {
      setBusy(true);
      try {
        await operation();
        await mutate();
        addToast({ title: success, color: "success" });
      } catch (operationError) {
        addToast({
          title: t("system.plugins.operationFailed"),
          text:
            operationError instanceof Error
              ? operationError.message
              : String(operationError),
          color: "danger",
        });
      } finally {
        setBusy(false);
      }
    },
    [addToast, mutate, t],
  );

  const inspect = async () => {
    if (!file) return;
    setBusy(true);
    try {
      setPreview(await previewPlugin(file));
      setApproved(false);
    } catch (previewError) {
      addToast({
        title: t("system.plugins.previewFailed"),
        text:
          previewError instanceof Error
            ? previewError.message
            : String(previewError),
        color: "danger",
      });
    } finally {
      setBusy(false);
    }
  };

  const isUpgrade = Boolean(
    preview &&
    plugins?.some((plugin) => plugin.manifest.id === preview.manifest.id),
  );

  return (
    <section className="space-y-6">
      <div>
        <p className="text-xs font-semibold uppercase tracking-widest text-brand">
          {t("system.plugins.eyebrow")}
        </p>
        <h2 className="mt-2 font-serif text-2xl font-medium text-foreground">
          {t("system.plugins.title")}
        </h2>
        <p className="mt-2 max-w-3xl text-sm leading-body text-muted">
          {t("system.plugins.description")}
        </p>
      </div>

      <Card
        icon={<Upload size={18} />}
        title={t("system.plugins.install.title")}
        description={t("system.plugins.install.description")}
      >
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <input
            type="file"
            accept=".sdwpkg,.zip"
            onChange={(event) => {
              setFile(event.target.files?.[0] ?? null);
              setPreview(null);
              setApproved(false);
            }}
            className="min-w-0 flex-1 text-sm text-muted file:mr-3 file:rounded-md file:border-0 file:bg-canvas file:px-3 file:py-2 file:text-sm file:text-foreground"
          />
          <Button
            variant="outline"
            disabled={!file || busy}
            onClick={() => void inspect()}
          >
            {busy ? <Spinner /> : <PackageCheck size={16} />}
            {t("system.plugins.install.inspect")}
          </Button>
        </div>

        {preview ? (
          <div className="mt-5 rounded-md border border-border-light bg-canvas/60 p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="font-medium text-foreground">
                  {preview.manifest.name} {preview.manifest.version}
                </p>
                <p className="mt-1 font-mono text-xs text-subtle">
                  {preview.manifest.id} · API {preview.manifest.apiVersion} ·
                  SHA-256 {preview.packageSha256}
                </p>
              </div>
              <span
                className={`inline-flex items-center gap-1.5 text-xs font-medium ${preview.isSignatureTrusted ? "text-success" : "text-warning"}`}
              >
                {preview.isSignatureTrusted ? (
                  <ShieldCheck size={15} />
                ) : (
                  <ShieldX size={15} />
                )}
                {preview.signatureStatus}
              </span>
            </div>

            <CapabilityList capabilities={preview.manifest.capabilities} />

            {preview.compatibilityErrors.length ? (
              <div className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-muted">
                {preview.compatibilityErrors.map((message) => (
                  <p key={message} className="flex gap-2">
                    <AlertTriangle
                      size={15}
                      className="mt-0.5 shrink-0 text-warning"
                    />
                    {message}
                  </p>
                ))}
              </div>
            ) : null}

            <label className="mt-4 flex items-start gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={approved}
                onChange={(event) => setApproved(event.target.checked)}
                className="mt-1"
              />
              <span>{t("system.plugins.install.approve")}</span>
            </label>
            <Button
              className="mt-4"
              disabled={!approved || busy}
              onClick={() =>
                void run(
                  async () => {
                    await installPlugin(preview, isUpgrade);
                    setPreview(null);
                    setFile(null);
                    setApproved(false);
                  },
                  t(
                    isUpgrade
                      ? "system.plugins.install.upgraded"
                      : "system.plugins.install.installed",
                  ),
                )
              }
            >
              <Plug size={16} />
              {t(
                isUpgrade
                  ? "system.plugins.install.upgrade"
                  : "system.plugins.install.install",
              )}
            </Button>
          </div>
        ) : null}
      </Card>

      <div className="space-y-3">
        <h3 className="font-serif text-lg font-medium text-foreground">
          {t("system.plugins.installed.title")}
        </h3>
        {error ? (
          <p className="text-sm text-error">{t("system.plugins.loadFailed")}</p>
        ) : !plugins ? (
          <div className="flex justify-center py-8">
            <Spinner />
          </div>
        ) : plugins.length === 0 ? (
          <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-sm text-muted">
            {t("system.plugins.installed.empty")}
          </p>
        ) : (
          plugins.map((plugin) => (
            <Card
              key={plugin.manifest.id}
              icon={
                plugin.isEnabled ? (
                  <CheckCircle2 size={18} className="text-success" />
                ) : (
                  <Plug size={18} />
                )
              }
              title={`${plugin.manifest.name} ${plugin.manifest.version}`}
              description={plugin.manifest.description ?? plugin.manifest.id}
              footer={
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <p className="text-xs text-subtle">
                    {t("system.plugins.installed.dataPolicy")}
                  </p>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      disabled={
                        busy ||
                        (!plugin.isEnabled &&
                          plugin.compatibilityErrors.length > 0)
                      }
                      onClick={() =>
                        void run(
                          () =>
                            setPluginEnabled(
                              plugin.manifest.id,
                              !plugin.isEnabled,
                            ),
                          t(
                            plugin.isEnabled
                              ? "system.plugins.disabled"
                              : "system.plugins.enabled",
                          ),
                        )
                      }
                    >
                      {t(
                        plugin.isEnabled
                          ? "system.plugins.installed.disable"
                          : "system.plugins.installed.enable",
                      )}
                    </Button>
                    <Button
                      variant="outline"
                      disabled={busy}
                      onClick={() => {
                        if (
                          window.confirm(
                            t("system.plugins.installed.uninstallConfirm", {
                              name: plugin.manifest.name,
                            }),
                          )
                        )
                          void run(
                            () => uninstallPlugin(plugin.manifest.id),
                            t("system.plugins.uninstalled"),
                          );
                      }}
                    >
                      <Trash2 size={15} />
                      {t("system.plugins.installed.uninstall")}
                    </Button>
                    <Button
                      variant="outline"
                      color="danger"
                      disabled={busy}
                      onClick={() => {
                        if (
                          window.confirm(
                            t("system.plugins.installed.deleteConfirm", {
                              name: plugin.manifest.name,
                            }),
                          )
                        )
                          void run(
                            () => uninstallPlugin(plugin.manifest.id, true),
                            t("system.plugins.deleted"),
                          );
                      }}
                    >
                      <Trash2 size={15} />
                      {t("system.plugins.installed.deleteData")}
                    </Button>
                  </div>
                </div>
              }
            >
              <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-muted">
                <span>
                  {t("system.plugins.installed.api", {
                    version: plugin.manifest.apiVersion,
                  })}
                </span>
                <span>
                  {t("system.plugins.installed.health", {
                    status: plugin.health.status,
                  })}
                </span>
                <span>
                  {t("system.plugins.installed.failures", {
                    count: plugin.health.consecutiveFailures,
                  })}
                </span>
              </div>
              <CapabilityList
                capabilities={plugin.approvedCapabilities}
                compact
              />
              {plugin.compatibilityErrors.map((message) => (
                <p
                  key={message}
                  className="mt-2 flex gap-2 text-sm text-warning"
                >
                  <AlertTriangle size={15} className="mt-0.5 shrink-0" />
                  {message}
                </p>
              ))}
              {plugin.health.lastError ? (
                <p className="mt-2 text-sm text-error">
                  {plugin.health.lastError}
                </p>
              ) : null}
            </Card>
          ))
        )}
      </div>
    </section>
  );
};

const CapabilityList: React.FC<{
  capabilities: PluginCapabilities;
  compact?: boolean;
}> = ({ capabilities, compact = false }) => {
  const { t } = useTranslation("settings");
  const values = [
    ...capabilities.networkDomains.map((domain) =>
      t("system.plugins.capabilities.network", { value: domain }),
    ),
    ...capabilities.fileRoots.map((root) =>
      t("system.plugins.capabilities.files", { value: root }),
    ),
    ...(capabilities.notifications
      ? [t("system.plugins.capabilities.notifications")]
      : []),
    ...(capabilities.downloadControl
      ? [t("system.plugins.capabilities.downloads")]
      : []),
    ...(capabilities.storageAccess
      ? [t("system.plugins.capabilities.storage")]
      : []),
    ...(capabilities.backgroundTasks
      ? [t("system.plugins.capabilities.background")]
      : []),
  ];
  return (
    <div className={compact ? "mt-2 flex flex-wrap gap-1.5" : "mt-4"}>
      {!compact ? (
        <p className="text-xs font-semibold uppercase tracking-wide text-subtle">
          {t("system.plugins.capabilities.title")}
        </p>
      ) : null}
      <div
        className={`${compact ? "contents" : "mt-2 flex flex-wrap gap-1.5"}`}
      >
        {(values.length ? values : [t("system.plugins.capabilities.none")]).map(
          (value) => (
            <span
              key={value}
              className="rounded-full border border-border-light bg-surface px-2 py-1 text-xs text-muted"
            >
              {value}
            </span>
          ),
        )}
      </div>
    </div>
  );
};
