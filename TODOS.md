# TODOS

## Backend

### Plugin System

- [ ] **实现 `IJavaScriptPluginLoader`** — `Framework/Plugin/IJavaScriptPluginLoader.cs` 定义了接口但无具体实现，需集成 ClearScript 或其他 JS 引擎加载插件
- [x] **实现 `IPluginServices`** — 已通过 `Plugin/PluginServices.cs` 实现，持有事件名到事件实例的映射
- [x] **实现 `IPluginEventRegister<TParam>` 和 `IPluginEventTrigger<TParams>`** — 已通过 `Plugin/PluginEvent.cs` 统一实现
- [x] **完成 `PluginHelper.InitializePlugin()`** — 已注册事件单例和 PluginServices 到 DI 容器

### FileStore

- [x] **重构 `IFileStore.Rename()`** — 已从 `IFileStore` 提取到独立的 `IFileOperator` 接口，`LocalFileStore` 不再需要实现 Rename，由下载器负责

### FileDownload

- [x] **实现 `FileDownloadClientProxy.CancelDownloadTask()`** — 已委托给底层 `_poxyObject` 调用
- [x] **完善 `RemoteTorrentDownloadClient.CancelDownloadTask()`** — 已传递 `deleteFiles` 参数给 qBittorrent API，由 qBittorrent 负责删除文件

## Frontend

### UI 功能缺失

- [x] **实现删除按钮功能** — 后端添加 `DELETE cancel/{id}` endpoint，前端添加 `cancelDownload` API 并绑定删除按钮（带确认对话框）
- [x] **实现"下载列表"导航** — 添加 `DownloadingPage`，注册 `/downloading` 路由，绑定导航链接
- [x] **实现"已下载"导航** — 添加 `DownloadedPage`，注册 `/downloaded` 路由，绑定导航链接

### 错误处理

- [x] **为下载/暂停/恢复操作添加用户可见的错误提示** — 添加 ToastProvider（基于 EuiGlobalToastList），操作失败时弹出错误 Toast
- [x] **添加路由错误页面** — 添加 `ErrorPage` 组件，所有路由使用独立错误页面替代 `MainPage`

### 代码清理

- [x] **移除调试日志** — 已移除 `MainPage.tsx` 中的 `console.log`
- [x] **添加更多语言支持** — 已添加 ja、ko、zh-tw locale
