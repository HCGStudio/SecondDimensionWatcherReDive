# TODOS

## Backend

### Plugin System (未实现)

- [ ] **实现 `IJavaScriptPluginLoader`** — `Framework/Plugin/IJavaScriptPluginLoader.cs` 定义了接口但无具体实现，需集成 ClearScript 或其他 JS 引擎加载插件
- [ ] **实现 `IPluginServices`** — `Framework/Plugin/IPluginServices.cs` 定义了插件服务接口，无实现且未注册到 DI 容器
- [ ] **实现 `IPluginEventRegister<TParam>`** — `Framework/Plugin/IPluginEventRegister.cs` 定义了插件事件注册接口，无实现
- [ ] **实现 `IPluginEventTrigger<TParams>`** — `Plugin/IPluginEventTrigger.cs` 在 `FileDownloadClientProxy` 中被注入但无实现，运行时会导致 DI 解析失败
- [ ] **完成 `PluginHelper.InitializePlugin()`** — `Plugin/PluginHelper.cs:5-8` 当前为空操作，需实现插件发现、加载和事件系统初始化

### FileStore

- [x] **重构 `IFileStore.Rename()`** — 已从 `IFileStore` 提取到独立的 `IFileOperator` 接口，`LocalFileStore` 不再需要实现 Rename，由下载器负责

### FileDownload

- [x] **实现 `FileDownloadClientProxy.CancelDownloadTask()`** — 已委托给底层 `_poxyObject` 调用
- [x] **完善 `RemoteTorrentDownloadClient.CancelDownloadTask()`** — 已传递 `deleteFiles` 参数给 qBittorrent API，由 qBittorrent 负责删除文件

## Frontend

### UI 功能缺失

- [ ] **实现删除按钮功能** — `src/compoments/AnimationInfoFooter.tsx:74-78` 删除按钮无 `onClick` 处理，且 `src/animation/utils.ts` 中缺少对应的 `deleteDownload` API 函数
- [ ] **实现"下载列表"导航** — `src/pages/PageTemplate.tsx:37` 导航链接无 `onClick` 处理，无对应路由和页面
- [ ] **实现"已下载"导航** — `src/pages/PageTemplate.tsx:38` 导航链接无 `onClick` 处理，无对应路由和页面

### 错误处理

- [ ] **为下载/暂停/恢复操作添加用户可见的错误提示** — `src/compoments/AnimationInfoFooter.tsx:26,42,46` 当前仅 `console.error`，用户无法感知操作失败
- [ ] **添加路由错误页面** — `src/Main.tsx:11,16` 路由错误时直接显示 `MainPage`，无错误反馈

### 代码清理

- [ ] **移除调试日志** — `src/pages/MainPage.tsx:35` 残留 `console.log("pageCount", pageCount)`
- [ ] **添加更多语言支持** — `src/utils/initDayjs.ts:7` TODO 注释标注需添加更多 locale，当前仅支持 `zh-cn`
