import React from "react";
import { useTranslation } from "react-i18next";

import { AlertTriangle, Plus, SlidersHorizontal, Trash2 } from "lucide-react";

import { useAccess } from "../auth/hooks";
import {
  SubscriptionPolicyModeBadge,
  SubscriptionPolicySheet,
} from "../components/SubscriptionPolicySheet";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { Spinner } from "../components/ui/Spinner";
import { Table, type TableColumn } from "../components/ui/Table";
import { IFeed } from "../feed/IFeed";
import { useFeeds } from "../feed/hooks";
import { addFeed, removeFeed } from "../feed/utils";
import { SeasonDiscovery } from "../season/SeasonDiscovery";
import { useSubscriptionPolicies } from "../subscriptionPolicy/hooks";
import { ISubscriptionPolicy } from "../subscriptionPolicy/types";
import { PageTemplate } from "./PageTemplate";

export const FeedsPage: React.FC = () => {
  const { t } = useTranslation(["feeds", "errors"]);
  const { canContentWrite } = useAccess();
  const { data: feeds, error, mutate } = useFeeds();
  const {
    data: policies,
    error: policiesError,
    mutate: mutatePolicies,
  } = useSubscriptionPolicies();
  const { addToast } = useToast();
  const [url, setUrl] = React.useState("");
  const [name, setName] = React.useState("");
  const [selectedFeed, setSelectedFeed] = React.useState<IFeed | null>(null);

  const policiesByFeed = React.useMemo(() => {
    const map = new Map<string, ISubscriptionPolicy>();
    policies?.forEach((policy) => map.set(policy.feedId, policy));
    return map;
  }, [policies]);

  const onAdd = React.useCallback(async () => {
    if (!url.trim()) return;
    try {
      await addFeed(url.trim(), name.trim() || undefined);
      setUrl("");
      setName("");
      await mutate();
      addToast({ title: t("feeds:toast.added"), color: "success" });
    } catch {
      addToast({ title: t("feeds:toast.addFailed"), color: "danger" });
    }
  }, [url, name, mutate, addToast, t]);

  const onRemove = React.useCallback(
    async (id: string) => {
      try {
        await removeFeed(id);
        await mutate();
        await mutatePolicies();
        addToast({ title: t("feeds:toast.deleted"), color: "success" });
      } catch {
        addToast({ title: t("feeds:toast.deleteFailed"), color: "danger" });
      }
    },
    [mutate, mutatePolicies, addToast, t],
  );

  const columns: TableColumn<IFeed>[] = [
    {
      field: "name",
      name: t("feeds:columns.name"),
      render: (value: string | undefined) => value || "-",
    },
    {
      field: "url",
      name: t("feeds:columns.url"),
      truncateText: true,
    },
    {
      field: "createdAt",
      name: t("feeds:columns.createdAt"),
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      name: t("feeds:automation.columns.policy"),
      render: (_value: unknown, item: IFeed) => {
        if (!policies && !policiesError) {
          return <Spinner size={14} />;
        }
        const policy = policiesByFeed.get(item.id);
        if (!policy) {
          return (
            <span className="text-xs text-subtle">
              {t("feeds:automation.status.notConfigured")}
            </span>
          );
        }

        const filterCount = countActiveFilters(policy);
        return (
          <div className="flex flex-wrap items-center gap-2">
            <SubscriptionPolicyModeBadge mode={policy.mode} />
            <span className="text-xs text-subtle">
              {t("feeds:automation.status.filterCount", {
                count: filterCount,
              })}
            </span>
          </div>
        );
      },
      width: "210px",
    },
    {
      name: t("feeds:columns.actions"),
      render: (_value: any, item: IFeed) =>
        canContentWrite ? (
          <div className="flex items-center gap-1">
            <Button
              variant="outline"
              size="sm"
              aria-label={t("feeds:automation.configureAria", {
                name: item.name || item.url,
              })}
              onClick={() => setSelectedFeed(item)}
            >
              <SlidersHorizontal size={15} />
              {t("feeds:automation.configure")}
            </Button>
            <Button
              variant="icon"
              color="danger"
              size="sm"
              aria-label={t("feeds:columns.delete")}
              onClick={() => onRemove(item.id)}
            >
              <Trash2 size={16} />
            </Button>
          </div>
        ) : null,
      width: "190px",
    },
  ];

  return (
    <PageTemplate>
      <SeasonDiscovery />
      <hr className="my-8 border-border-light" />
      <h2 className="mb-4 font-serif text-xl font-medium text-foreground">
        {t("feeds:manualSubscribe")}
      </h2>
      {canContentWrite ? (
        <div className="flex items-end gap-4">
          <FormRow label={t("feeds:urlLabel")} className="flex-1">
            <Input
              placeholder={t("feeds:urlPlaceholder")}
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </FormRow>
          <FormRow label={t("feeds:nameLabel")}>
            <Input
              placeholder={t("feeds:namePlaceholder")}
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </FormRow>
          <FormRow hasEmptyLabelSpace>
            <Button onClick={onAdd}>
              <Plus size={16} />
              {t("feeds:add")}
            </Button>
          </FormRow>
        </div>
      ) : null}
      <div className="mt-8">
        <div className="mb-4 flex items-start gap-3">
          <div className="mt-0.5 rounded-md bg-brand/10 p-2 text-brand">
            <SlidersHorizontal size={18} />
          </div>
          <div>
            <h2 className="font-serif text-xl font-medium text-foreground">
              {t("feeds:automation.title")}
            </h2>
            <p className="mt-1 max-w-3xl text-sm leading-body text-muted">
              {t("feeds:automation.intro")}
            </p>
          </div>
        </div>
        {error ? (
          <EmptyPrompt
            icon={<AlertTriangle size={48} />}
            title={<h2>{t("errors:loadFailed")}</h2>}
            body={<p>{t("feeds:loadFailed")}</p>}
          />
        ) : feeds && feeds.length > 0 ? (
          <Table items={feeds} columns={columns} />
        ) : feeds ? (
          <EmptyPrompt
            title={<h2>{t("feeds:empty.title")}</h2>}
            body={<p>{t("feeds:empty.body")}</p>}
          />
        ) : null}
      </div>
      {canContentWrite ? (
        <SubscriptionPolicySheet
          feed={selectedFeed}
          initialPolicy={
            selectedFeed ? policiesByFeed.get(selectedFeed.id) : undefined
          }
          onOpenChange={(open) => {
            if (!open) setSelectedFeed(null);
          }}
          onPolicyChanged={() => mutatePolicies()}
        />
      ) : null}
    </PageTemplate>
  );
};

const countActiveFilters = (policy: ISubscriptionPolicy) =>
  [
    policy.subtitleGroups.length > 0,
    policy.resolutions.length > 0,
    policy.codecs.length > 0,
    policy.languages.length > 0,
    policy.excludedKeywords.length > 0,
    policy.minSizeBytes != null || policy.maxSizeBytes != null,
  ].filter(Boolean).length;
