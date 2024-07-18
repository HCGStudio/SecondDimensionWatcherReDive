import {
  EuiHeader,
  EuiHeaderLink,
  EuiHeaderLinks,
  EuiHeaderLogo,
  EuiHeaderSection,
  EuiHeaderSectionItem,
  EuiPageTemplate,
} from "@elastic/eui";
import React from "react";
import { useNavigate } from "react-router-dom";

import { useLoginStatus } from "../auth/hooks";

export interface IPageTemplateProps extends React.PropsWithChildren {}

export const PageTemplate: React.FC<IPageTemplateProps> = ({ children }) => {
  const { data: status } = useLoginStatus();
  const navigate = useNavigate();
  return (
    <EuiPageTemplate>
      <EuiHeader>
        <EuiHeaderSection grow={false}>
          <EuiHeaderSectionItem>
            <EuiHeaderLogo iconType="kubernetesPod">二次元观测器</EuiHeaderLogo>
          </EuiHeaderSectionItem>
          <EuiHeaderSectionItem>
            <EuiHeaderLinks>
              <EuiHeaderLink
                iconType="home"
                onClick={() => {
                  navigate("/");
                }}
              >
                主页
              </EuiHeaderLink>
              <EuiHeaderLink iconType="download">下载列表</EuiHeaderLink>
              <EuiHeaderLink iconType="list">已下载</EuiHeaderLink>
            </EuiHeaderLinks>
          </EuiHeaderSectionItem>
        </EuiHeaderSection>
        <EuiHeaderSection>
          <EuiHeaderSectionItem>
            {status ? (
              <EuiHeaderLinks>
                <EuiHeaderLink
                  onClick={() => {
                    localStorage.clear();
                    location.reload();
                  }}
                >
                  注销
                </EuiHeaderLink>
              </EuiHeaderLinks>
            ) : (
              <EuiHeaderLinks>
                <EuiHeaderLink
                  iconType="user"
                  onClick={() => {
                    navigate("/login");
                  }}
                >
                  登录
                </EuiHeaderLink>
              </EuiHeaderLinks>
            )}
          </EuiHeaderSectionItem>
        </EuiHeaderSection>
      </EuiHeader>
      <EuiPageTemplate.Section>{children}</EuiPageTemplate.Section>
    </EuiPageTemplate>
  );
};
