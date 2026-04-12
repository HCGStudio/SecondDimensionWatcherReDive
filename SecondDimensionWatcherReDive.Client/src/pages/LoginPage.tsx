import React from "react";
import { useNavigate } from "react-router-dom";

import { useAllowRegister, useLoginStatus } from "../auth/hooks";
import { setAuthResult } from "../auth/httpClient";
import { login, register } from "../auth/utils";
import { Button } from "../components/ui/Button";
import { FormRow } from "../components/ui/FormRow";
import { PasswordInput } from "../components/ui/PasswordInput";
import { PageTemplate } from "./PageTemplate";

export const LoginPage: React.FC = () => {
  const { data: registerInfo } = useAllowRegister();
  const { data: status } = useLoginStatus();
  const [password, setPassword] = React.useState("");
  const [passwordConfirm, setPasswordConfirm] = React.useState("");
  const [loginResult, setLoginResult] = React.useState(false);
  const navgiate = useNavigate();

  const onPasswordChange: React.ChangeEventHandler<HTMLInputElement> = (ev) => {
    setPassword(ev.target.value);
  };
  const onPasswordConfirmChange: React.ChangeEventHandler<HTMLInputElement> = (
    ev,
  ) => {
    setPasswordConfirm(ev.target.value);
  };
  const onRegister = React.useCallback(() => {
    if (password !== passwordConfirm) return;
    register(password).then((r) => {
      if (r?.success) {
        setAuthResult(r);
        navgiate("/");
      }
    });
  }, [password, passwordConfirm, navgiate]);

  const onLogin = React.useCallback(() => {
    login(password)
      .then((r) => {
        if (r?.success) {
          setAuthResult(r);
          navgiate("/");
        } else {
          setLoginResult(true);
        }
      })
      .catch(() => {
        setLoginResult(true);
      });
  }, [password, navgiate]);

  React.useEffect(() => {
    if (status) navgiate("/");
  }, [navgiate, status]);

  return (
    <PageTemplate>
      <div className="mx-auto max-w-md">
        {status ? null : registerInfo?.allow ? (
          <>
            <h2 className="font-serif text-2xl font-medium leading-heading">
              请设置密码
            </h2>
            <p className="mt-2 text-sm text-muted leading-body">
              您是第一次使用二次元观测器，请设置密码。
            </p>
            <div className="mt-6 space-y-4">
              <FormRow label="密码">
                <PasswordInput
                  placeholder="请输入密码"
                  value={password}
                  onChange={onPasswordChange}
                />
              </FormRow>
              <FormRow
                label="重复密码"
                isInvalid={password !== passwordConfirm}
                error={["两次密码输入不一致"]}
              >
                <PasswordInput
                  placeholder="重复密码"
                  value={passwordConfirm}
                  onChange={onPasswordConfirmChange}
                  isInvalid={password !== passwordConfirm}
                />
              </FormRow>
              <Button onClick={onRegister}>注册</Button>
            </div>
          </>
        ) : (
          <>
            <h2 className="font-serif text-2xl font-medium leading-heading">
              欢迎回来
            </h2>
            <div className="mt-6 space-y-4">
              <FormRow
                label="密码"
                isInvalid={loginResult}
                error={["密码不正确"]}
              >
                <PasswordInput
                  placeholder="请输入密码"
                  value={password}
                  onChange={onPasswordChange}
                  isInvalid={loginResult}
                />
              </FormRow>
              <Button onClick={onLogin}>登录</Button>
            </div>
          </>
        )}
      </div>
    </PageTemplate>
  );
};
