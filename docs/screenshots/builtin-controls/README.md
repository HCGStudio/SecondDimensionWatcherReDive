# 内置控件前后对比

截图采集于 2026-09-27，使用同一 Chromium / Xvfb 会话、1440 × 1080 屏幕、DPR 1 和中文界面，运行生产构建及仓库 mock API。浏览器全屏内容区域为 1439 × 1079；截图保留完整屏幕以呈现浏览器原生弹窗和下拉菜单。

修复前基于 `1a68e8a`；修复后基于本 PR。日期/会话时间来自 mock 数据。

| 场景 | 操作位置 | 修复前 | 修复后 |
| --- | --- | --- | --- |
| 输入弹窗 | `/account` → 编辑名称、头像或 PIN → 档案名称 | [截图](before-prompt.png) | [截图](after-prompt.png) |
| 确认弹窗 | 同一编辑流程 → 保留名称与头像 → 是否重设 PIN | [截图](before-confirm.png) | [截图](after-confirm.png) |
| 下拉菜单 | `/account` → 创建用户 → 角色 | [截图](before-select.png) | [截图](after-select.png) |
| 日期选择 | `/settings?section=access` → WebDAV 凭据 → 到期日期 | [截图](before-date.png) | [截图](after-date.png) |
