import { Copy, HardDrive } from "lucide-react";
import React from "react";
import { Trans, useTranslation } from "react-i18next";

import { useToast } from "./ToastProvider";
import { Button } from "./ui/Button";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "./ui/Sheet";

const WEBDAV_USERNAME = "sdwuser";

const buildEndpoint = (): string => {
  if (typeof window === "undefined") return "/webdav/";
  return `${window.location.origin}/webdav/`;
};

const codeNode = <code className="rounded bg-canvas px-1 font-mono text-xs" />;
const accentNode = <span className="text-foreground" />;

export const WebDavAccessSheet: React.FC = () => {
  const { t } = useTranslation("files");
  const endpoint = React.useMemo(buildEndpoint, []);
  const { addToast } = useToast();

  const copy = React.useCallback(
    async (value: string, label: string) => {
      try {
        await navigator.clipboard.writeText(value);
        addToast({
          title: t("webdav.copy.success", { label }),
          color: "success",
        });
      } catch {
        addToast({ title: t("webdav.copy.failed"), color: "danger" });
      }
    },
    [addToast, t],
  );

  return (
    <Sheet>
      <SheetTrigger asChild>
        <Button variant="outline" size="sm">
          <HardDrive size={16} />
          {t("webdav.button")}
        </Button>
      </SheetTrigger>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>{t("webdav.title")}</SheetTitle>
          <p className="mt-1 text-sm leading-body text-muted">
            {t("webdav.description")}
          </p>
        </SheetHeader>
        <SheetBody className="space-y-6">
          <section>
            <h4 className="mb-2 font-serif text-base font-medium text-foreground">
              {t("webdav.connection")}
            </h4>
            <div className="space-y-2">
              <CredentialRow
                label={t("webdav.address")}
                value={endpoint}
                onCopy={() => copy(endpoint, t("webdav.address"))}
              />
              <CredentialRow
                label={t("webdav.username")}
                value={WEBDAV_USERNAME}
                onCopy={() => copy(WEBDAV_USERNAME, t("webdav.username"))}
              />
              <div className="rounded-md border border-border-light bg-canvas px-3 py-2 text-sm leading-body">
                <div className="text-xs uppercase tracking-wide text-subtle">
                  {t("webdav.password")}
                </div>
                <div className="mt-0.5 text-foreground">
                  <Trans
                    i18nKey="webdav.passwordHelp"
                    t={t}
                    values={{ key: "Password:Value" }}
                    components={{
                      code: <code className="rounded bg-surface px-1 font-mono text-xs" />,
                    }}
                  />
                </div>
              </div>
            </div>
          </section>

          <section>
            <h4 className="mb-2 font-serif text-base font-medium text-foreground">
              {t("webdav.notes")}
            </h4>
            <ul className="space-y-1 text-sm leading-body text-muted">
              <li>
                ·{" "}
                <Trans
                  i18nKey="webdav.noteReadOnly"
                  t={t}
                  components={{ accent: accentNode }}
                />
              </li>
              <li>· {t("webdav.noteBasicAuth")}</li>
              <li>· {t("webdav.notePathSame")}</li>
            </ul>
          </section>

          <section>
            <h4 className="mb-2 font-serif text-base font-medium text-foreground">
              {t("webdav.examples")}
            </h4>
            <div className="space-y-3">
              <PlatformGuide
                title={t("webdav.macos.title")}
                steps={[
                  t("webdav.macos.step1"),
                  <Trans
                    key="macos-2"
                    i18nKey="webdav.macos.step2"
                    t={t}
                    values={{ endpoint }}
                    components={{ code: codeNode }}
                  />,
                  <Trans
                    key="macos-3"
                    i18nKey="webdav.macos.step3"
                    t={t}
                    values={{ username: WEBDAV_USERNAME }}
                    components={{ code: codeNode }}
                  />,
                ]}
              />
              <PlatformGuide
                title={t("webdav.windows.title")}
                steps={[
                  t("webdav.windows.step1"),
                  <Trans
                    key="win-2"
                    i18nKey="webdav.windows.step2"
                    t={t}
                    values={{ endpoint }}
                    components={{ code: codeNode }}
                  />,
                  t("webdav.windows.step3"),
                ]}
              />
              <PlatformGuide
                title={t("webdav.mobile.title")}
                steps={[
                  t("webdav.mobile.step1"),
                  <Trans
                    key="mobile-2"
                    i18nKey="webdav.mobile.step2"
                    t={t}
                    values={{ endpoint }}
                    components={{ code: codeNode }}
                  />,
                  <Trans
                    key="mobile-3"
                    i18nKey="webdav.mobile.step3"
                    t={t}
                    values={{ username: WEBDAV_USERNAME }}
                    components={{ code: codeNode }}
                  />,
                ]}
              />
            </div>
          </section>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
};

interface CredentialRowProps {
  label: string;
  value: string;
  onCopy: () => void;
}

const CredentialRow: React.FC<CredentialRowProps> = ({
  label,
  value,
  onCopy,
}) => {
  const { t } = useTranslation("files");
  return (
    <div className="flex items-center gap-2 rounded-md border border-border-light bg-canvas px-3 py-2">
      <div className="min-w-0 flex-1">
        <div className="text-xs uppercase tracking-wide text-subtle">{label}</div>
        <div className="truncate font-mono text-sm text-foreground" title={value}>
          {value}
        </div>
      </div>
      <Button
        variant="icon"
        size="sm"
        aria-label={t("webdav.copy.copyLabel", { label })}
        onClick={onCopy}
      >
        <Copy size={14} />
      </Button>
    </div>
  );
};

interface PlatformGuideProps {
  title: string;
  steps: React.ReactNode[];
}

const PlatformGuide: React.FC<PlatformGuideProps> = ({ title, steps }) => (
  <div className="rounded-md border border-border-light bg-surface px-4 py-3">
    <div className="mb-1.5 font-serif text-sm font-medium text-foreground">
      {title}
    </div>
    <ol className="ml-5 list-decimal space-y-0.5 text-sm leading-body text-muted">
      {steps.map((step, i) => (
        <li key={i}>{step}</li>
      ))}
    </ol>
  </div>
);
