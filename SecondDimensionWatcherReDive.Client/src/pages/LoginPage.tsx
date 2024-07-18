import {
  EuiButton,
  EuiFieldPassword,
  EuiFormRow,
  EuiSpacer,
  EuiText,
} from "@elastic/eui";
import React from "react";
import { useNavigate } from "react-router-dom";

import { useAllowRegister, useLoginStatus } from "../auth/hooks";
import { setAuthResult } from "../auth/httpClient";
import { login, register } from "../auth/utils";
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
      {status ? null : registerInfo?.allow ? (
        <>
          <EuiText>
            <h2>请设置密码</h2>
            <EuiSpacer />
            您是第一次使用二次元观测器，请设置密码。
          </EuiText>
          <EuiSpacer size="s" />
          <EuiFormRow label="密码">
            <EuiFieldPassword
              placeholder="请输入密码"
              value={password}
              onChange={onPasswordChange}
              type="dual"
            />
          </EuiFormRow>
          <EuiSpacer size="s" />
          <EuiFormRow
            label="重复密码"
            isInvalid={password !== passwordConfirm}
            error={["两次密码输入不一致"]}
          >
            <EuiFieldPassword
              placeholder="重复密码"
              value={passwordConfirm}
              onChange={onPasswordConfirmChange}
              type="dual"
              isInvalid={password !== passwordConfirm}
            />
          </EuiFormRow>
          <EuiSpacer size="s" />
          <EuiButton onClick={onRegister}>注册</EuiButton>
        </>
      ) : (
        <>
          <EuiText>
            <h2>欢迎回来</h2>
            <EuiSpacer />
          </EuiText>
          <EuiFormRow
            label="密码"
            isInvalid={loginResult}
            error={["密码不正确"]}
          >
            <EuiFieldPassword
              placeholder="请输入密码"
              value={password}
              onChange={onPasswordChange}
              type="dual"
              isInvalid={loginResult}
            />
          </EuiFormRow>
          <EuiSpacer size="s" />
          <EuiButton onClick={onLogin}>登录</EuiButton>
        </>
      )}
    </PageTemplate>
  );
};
