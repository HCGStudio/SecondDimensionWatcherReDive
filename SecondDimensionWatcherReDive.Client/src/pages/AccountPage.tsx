import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { mutate as mutateAll } from "swr";

import { KeyRound, Monitor, Plus, Shield, UserRound } from "lucide-react";

import {
  createProfile,
  createUser,
  revokeSession,
  updateProfile,
  updateUserAccess,
} from "../accounts/api";
import {
  useAllSessions,
  useProfiles,
  useSessions,
  useUsers,
} from "../accounts/hooks";
import { IAccountSession } from "../accounts/types";
import { IAuthProfile, UserRole } from "../auth/IAuthResult";
import { useLoginStatus } from "../auth/hooks";
import { clearAuthForSession } from "../auth/httpClient";
import { reauthenticate, switchProfile } from "../auth/utils";
import { Button } from "../components/ui/Button";
import { Card } from "../components/ui/Card";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { PasswordInput } from "../components/ui/PasswordInput";
import { PageTemplate } from "./PageTemplate";

const roles: UserRole[] = ["Admin", "Member", "Viewer"];

export const AccountPage: React.FC = () => {
  const { t } = useTranslation("accounts");
  const navigate = useNavigate();
  const { data: status, mutate: mutateStatus } = useLoginStatus();
  const isAdmin = status?.role === "Admin";
  const canCreateProfile =
    status?.role === "Admin" || status?.role === "Member";
  const { data: profiles, mutate: mutateProfiles } = useProfiles();
  const { data: sessions, mutate: mutateSessions } = useSessions();
  const { data: users, mutate: mutateUsers } = useUsers(isAdmin);
  const { data: allSessions, mutate: mutateAllSessions } =
    useAllSessions(isAdmin);

  const [profileName, setProfileName] = React.useState("");
  const [profileAvatar, setProfileAvatar] = React.useState("");
  const [profilePin, setProfilePin] = React.useState("");
  const [username, setUsername] = React.useState("");
  const [password, setPassword] = React.useState("");
  const [newUserProfile, setNewUserProfile] = React.useState("Home");
  const [newUserRole, setNewUserRole] = React.useState<UserRole>("Member");
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const run = React.useCallback(
    async (operation: () => Promise<unknown>) => {
      if (busy) return;
      setBusy(true);
      setError(null);
      try {
        await operation();
      } catch (operationError) {
        setError(
          operationError instanceof Error
            ? operationError.message
            : t("failed"),
        );
      } finally {
        setBusy(false);
      }
    },
    [busy, t],
  );

  const stepUp = React.useCallback(async (): Promise<boolean> => {
    const value = window.prompt(t("reauthPrompt"));
    if (!value) return false;
    await reauthenticate(value);
    await mutateStatus();
    return true;
  }, [mutateStatus, t]);

  const activate = (profile: IAuthProfile) =>
    run(async () => {
      const pin = profile.hasPin ? window.prompt(t("pinPrompt")) : undefined;
      if (profile.hasPin && pin === null) return;
      await switchProfile(profile.id, pin || undefined);
      await mutateAll(() => true, undefined, { revalidate: false });
      window.location.assign("/");
    });

  const saveCurrentProfile = (profile: IAuthProfile) =>
    run(async () => {
      const name = window.prompt(t("profileName"), profile.name);
      if (!name) return;
      const avatar = window.prompt(t("avatar"), profile.avatar ?? "");
      if (avatar === null) return;
      const replacePin = window.confirm(t("replacePinPrompt"));
      let currentPin: string | undefined;
      let pin: string | undefined;
      if (replacePin) {
        if (profile.hasPin) {
          const value = window.prompt(t("currentPin"));
          if (value === null) return;
          currentPin = value;
        } else if (!(await stepUp())) {
          return;
        }
        const value = window.prompt(t("newPin"));
        if (value === null) return;
        pin = value;
      }
      await updateProfile(profile.id, {
        name,
        avatar: avatar || undefined,
        currentPin,
        pin,
        replacePin,
      });
      await Promise.all([mutateProfiles(), mutateStatus()]);
    });

  const addProfile = () =>
    run(async () => {
      await createProfile({
        name: profileName,
        avatar: profileAvatar || undefined,
        pin: profilePin || undefined,
      });
      setProfileName("");
      setProfileAvatar("");
      setProfilePin("");
      await Promise.all([mutateProfiles(), mutateStatus()]);
    });

  const removeSession = (session: IAccountSession, asAdmin = false) =>
    run(async () => {
      if (asAdmin && !(await stepUp())) return;
      await revokeSession(session.id, asAdmin);
      if (session.isCurrent) {
        if (clearAuthForSession(session.id)) {
          navigate("/login", { replace: true });
        }
        return;
      }
      await Promise.all([mutateSessions(), mutateAllSessions()]);
    });

  const addUser = () =>
    run(async () => {
      if (!(await stepUp())) return;
      await createUser({
        username,
        password,
        role: newUserRole,
        profileName: newUserProfile,
      });
      setUsername("");
      setPassword("");
      await mutateUsers();
    });

  return (
    <PageTemplate>
      <header className="mb-8">
        <h1 className="font-serif text-2xl font-medium text-foreground">
          {t("title")}
        </h1>
        <p className="mt-2 text-sm text-muted">
          {status?.username} · {status?.role}
        </p>
        {error ? <p className="mt-2 text-sm text-error">{error}</p> : null}
      </header>

      <section>
        <h2 className="mb-4 font-serif text-xl text-foreground">
          {t("profiles")}
        </h2>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {profiles?.map((profile) => {
            const active = profile.id === status?.profileId;
            return (
              <Card
                key={profile.id}
                icon={
                  profile.avatar ? (
                    <img
                      src={profile.avatar}
                      alt=""
                      className="h-9 w-9 rounded-full object-cover"
                    />
                  ) : (
                    <UserRound size={22} />
                  )
                }
                title={profile.name}
                description={`${active ? t("active") : t("available")} · ${
                  profile.hasPin ? t("pinProtected") : t("noPin")
                }`}
                footer={
                  active ? (
                    canCreateProfile ? (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={busy}
                        onClick={() => void saveCurrentProfile(profile)}
                      >
                        {t("editProfile")}
                      </Button>
                    ) : null
                  ) : (
                    <Button
                      size="sm"
                      disabled={busy}
                      onClick={() => void activate(profile)}
                    >
                      {t("switchProfile")}
                    </Button>
                  )
                }
              />
            );
          })}
        </div>

        {canCreateProfile ? (
          <Card
            className="mt-5"
            icon={<Plus size={18} />}
            title={t("createProfile")}
          >
            <div className="grid gap-3 md:grid-cols-4 md:items-end">
              <FormRow label={t("profileName")}>
                <Input
                  value={profileName}
                  onChange={(event) => setProfileName(event.target.value)}
                />
              </FormRow>
              <FormRow label={t("avatar")}>
                <Input
                  value={profileAvatar}
                  onChange={(event) => setProfileAvatar(event.target.value)}
                />
              </FormRow>
              <FormRow label={t("pinOptional")}>
                <PasswordInput
                  value={profilePin}
                  inputMode="numeric"
                  onChange={(event) => setProfilePin(event.target.value)}
                />
              </FormRow>
              <FormRow hasEmptyLabelSpace>
                <Button
                  disabled={busy || !profileName.trim()}
                  onClick={() => void addProfile()}
                >
                  {t("create")}
                </Button>
              </FormRow>
            </div>
          </Card>
        ) : null}
      </section>

      <SessionSection
        title={t("mySessions")}
        sessions={sessions}
        busy={busy}
        onRevoke={(session) => void removeSession(session)}
      />

      {isAdmin ? (
        <>
          <section className="mt-10">
            <h2 className="mb-4 font-serif text-xl text-foreground">
              {t("users")}
            </h2>
            <Card icon={<Shield size={18} />} title={t("createUser")}>
              <div className="grid gap-3 md:grid-cols-5 md:items-end">
                <FormRow label={t("username")}>
                  <Input
                    value={username}
                    onChange={(event) => setUsername(event.target.value)}
                  />
                </FormRow>
                <FormRow label={t("password")}>
                  <PasswordInput
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                </FormRow>
                <FormRow label={t("profileName")}>
                  <Input
                    value={newUserProfile}
                    onChange={(event) => setNewUserProfile(event.target.value)}
                  />
                </FormRow>
                <FormRow label={t("role")}>
                  <select
                    className="rounded-lg border border-border bg-surface px-3 py-2 text-sm"
                    value={newUserRole}
                    onChange={(event) =>
                      setNewUserRole(event.target.value as UserRole)
                    }
                  >
                    {roles.map((role) => (
                      <option key={role}>{role}</option>
                    ))}
                  </select>
                </FormRow>
                <FormRow hasEmptyLabelSpace>
                  <Button
                    disabled={busy || !username || !password}
                    onClick={() => void addUser()}
                  >
                    {t("create")}
                  </Button>
                </FormRow>
              </div>
            </Card>
            <div className="mt-4 space-y-3">
              {users?.map((user) => (
                <div
                  key={user.id}
                  className="flex flex-wrap items-center gap-3 rounded-md border border-border bg-surface p-4"
                >
                  <div className="min-w-40 flex-1">
                    <div className="font-medium text-foreground">
                      {user.username}
                    </div>
                    <div className="text-xs text-muted">
                      {user.profiles.map((profile) => profile.name).join(", ")}
                    </div>
                  </div>
                  <select
                    className="rounded-md border border-border bg-surface px-2 py-1.5 text-sm"
                    value={user.role}
                    disabled={busy}
                    onChange={(event) =>
                      void run(async () => {
                        if (!(await stepUp())) return;
                        await updateUserAccess(
                          user.id,
                          event.target.value as UserRole,
                          user.isDisabled,
                        );
                        await mutateUsers();
                      })
                    }
                  >
                    {roles.map((role) => (
                      <option key={role}>{role}</option>
                    ))}
                  </select>
                  <Button
                    variant="outline"
                    color={user.isDisabled ? "success" : "danger"}
                    size="sm"
                    disabled={busy}
                    onClick={() =>
                      void run(async () => {
                        if (!(await stepUp())) return;
                        await updateUserAccess(
                          user.id,
                          user.role,
                          !user.isDisabled,
                        );
                        await mutateUsers();
                      })
                    }
                  >
                    {user.isDisabled ? t("enable") : t("disable")}
                  </Button>
                </div>
              ))}
            </div>
          </section>

          <SessionSection
            title={t("allSessions")}
            sessions={allSessions}
            busy={busy}
            onRevoke={(session) => void removeSession(session, true)}
          />

          <Card
            className="mt-10"
            icon={<KeyRound size={18} />}
            title={t("deviceTokens")}
          >
            <p className="text-sm text-muted">{t("deviceTokensHelp")}</p>
            <Button
              className="mt-3"
              variant="outline"
              onClick={() => navigate("/settings?section=access")}
            >
              {t("manageDeviceTokens")}
            </Button>
          </Card>
        </>
      ) : null}
    </PageTemplate>
  );
};

const SessionSection: React.FC<{
  title: string;
  sessions?: IAccountSession[];
  busy: boolean;
  onRevoke: (session: IAccountSession) => void;
}> = ({ title, sessions, busy, onRevoke }) => {
  const { t } = useTranslation("accounts");
  return (
    <section className="mt-10">
      <h2 className="mb-4 font-serif text-xl text-foreground">{title}</h2>
      <div className="space-y-3">
        {sessions?.map((session) => (
          <div
            key={session.id}
            className="flex items-center gap-3 rounded-md border border-border bg-surface p-4"
          >
            <Monitor size={18} className="text-muted" />
            <div className="min-w-0 flex-1">
              <div className="font-medium text-foreground">
                {session.deviceName || t("unknownDevice")}
                {session.isCurrent ? ` · ${t("current")}` : ""}
              </div>
              <div className="text-xs text-muted">
                {session.username} / {session.profileName} ·{" "}
                {new Date(session.lastSeenAt).toLocaleString()}
                {session.revokedAt ? ` · ${t("revoked")}` : ""}
              </div>
            </div>
            {!session.revokedAt ? (
              <Button
                variant="outline"
                color="danger"
                size="sm"
                disabled={busy}
                onClick={() => onRevoke(session)}
              >
                {t("revoke")}
              </Button>
            ) : null}
          </div>
        ))}
      </div>
    </section>
  );
};
