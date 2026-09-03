# sing-box 1.14+ 原生 gRPC API 功能清单与接入建议

> 依据：`src/carton.Core/Protos/started_service.proto`（与本地 `sing-box-dashboard/proto` 一致；
> 与 v1.14.0 release tag 比对，carton 用到的消息字段编号完全兼容）+ v1.14.0 服务端源码
> （`daemon/started_service.go`）+ 官方 dashboard 消费方式。
> **结论先行**：P0 已接入并修复；P1 按需小成本接入；P2 建议下个版本接入；
> P3 观望；P4 明确不接入。

## P0 —— 已接入（本轮修复后状态）

| RPC | carton 用法 | 备注 |
| --- | --- | --- |
| `GetVersion` | `IsReachableAsync` 探活（Unauthenticated 也算可达，对齐旧 401 语义） | `apiVersion` 字段还没用，见 P2 |
| `GetClashModeStatus` / `SetClashMode` | Dashboard 模式切换 | **依赖最小 clash_api 块**（Phase 0.3），否则 NotFound |
| `SubscribeGroups` / `SelectOutbound` / `URLTest` | 代理组选择与测速 | 测速已改为"基线→触发→等新鲜快照" |
| `SubscribeConnections` / `CloseConnection` / `CloseAllConnections` | 连接页 | 初始快照含 ClosedAt>0 的已关连接，已过滤 |
| `SubscribeLog` | 日志监控 | `reset=true` 已接通（KernelLogsReset 事件），`ClearLogs` 可后续接到"清空日志"按钮 |
| `SubscribeStatus` | 流量/内存监控 | interval 必须传**纳秒**（1s=1e9）；单条流聚合 uplink/downlink/uplinkTotal/goroutines/连接数 |

**契约要点**（踩过的坑，务必保持）：
1. `SubscribeStatusRequest.Interval` / `SubscribeConnectionsRequest.Interval` 单位是 **纳秒**
   （Go `time.Duration`），≤0 时服务端默认 1s。传 `1000` = 1µs，内核每微秒推一条。
2. `URLTest`：传**组 tag**（selector/urltest 都行，v1.14.0 也支持单个 outbound），
   服务端 `go` 异步执行，结果经 urltest 历史钩子从 `SubscribeGroups` 推回；**RPC 返回 ≠ 测试完成**。
   客户端必须用 `UrlTestTime`（unix 秒）判断新鲜度，不能拿"任意 delay>0"当完成。
3. `SubscribeConnections` 首条消息 = 全量快照（`reset=true`，活跃+最近关闭各为 NEW 事件，
   关闭连接带 `ClosedAt`），之后是 NEW/UPDATE/CLOSED 差量。
4. clash 模式三件套（`GetClashModeStatus`/`SetClashMode`/`SubscribeClashMode`）**要求配置里有
   `experimental.clash_api`**（空 `external_controller` 即可，不开 REST 端口）；没有它，
   `clash_mode` 路由规则也永不匹配（v1.14.0 `route/rule/rule_item_clash_mode.go`）。

## P1 —— 已具备、按需零成本接入

| RPC / 能力 | 用途 | 接入成本 |
| --- | --- | --- |
| `GetStartedAt` | 精确的内核运行时长（Dashboard 现在用客户端记时） | 极低 |
| `GetDefaultLogLevel` | 日志页显示/切换内核实际日志级别 | 极低 |
| `ClearLogs` | 日志页"清空"按钮（清内核缓冲） | 极低 |
| ~~`SetGroupExpand`~~ | 已提升至 P2 接入 ✅ | — |
| `SubscribeClashMode` | 模式变更推送（现在是 GetClashModeStatus 轮询） | 低，Phase 1 顺带 |
| `Status` 聚合字段 | `connectionsIn`（替代连接计数轮询）、`goroutines`（健康度）、`uplinkTotal/downlinkTotal`（精确总流量，替代客户端累加 `+= Uplink`） | 低，Phase 1 顺带 |

## P2 —— 已接入 ✅

| RPC / 能力 | 用途 | 状态 |
| --- | --- | --- |
| `GetVersion().apiVersion` | **能力门控**：`SingBoxManager.SupportsApiFeature(feature)`，官方 dashboard 的 MIN_API_VERSION 表（usbip=2、oc/vpn=3、taildrop=4） | ✅ 已接入 + wire 测试；attach 外部实例时自动刷新版本 |
| `GetDeprecatedWarnings` | 配置体检：内核报告的弃用警告（`deprecatedVersion`/`scheduledVersion`/`migrationLink`） | ✅ 已接入：内核启动后自动拉取并写入用户日志 |
| `SetGroupExpand` | 组展开状态服务端持久化（cache.db），跨重启/多端同步 | ✅ 已接入（从原 P1 提升） |
| 连接差量 | 连接页实时速率/关闭时间（`uplinkDelta/downlinkDelta`） | ✅ 数据层就绪（Streaming 合并引擎），UI 速率列待后续按需呈现 |

## P3 —— 观望（有场景再接）

| RPC / 能力 | 语义与不接入理由 |
| --- | --- |
| `StartNetworkQualityTest` | 真实带宽测试（下载/上传容量、RPM、空闲延迟，流式进度+`isFinal`）。**杀手级功能**但：走 `configURL` 测速服务器、串行/并发可配、运行时长由 `maxRuntimeSeconds` 决定——UI 需要"测试中"遮罩+进度+取消，且会占用带宽。建议等 Phase 1 后做独立"网络体检"入口（Proxies 页每节点或 Tools 面板），不要混进现有测速按钮。 |
| `StartSTUNTest` | NAT 类型诊断（Full-cone/Symmetric、公网映射、延迟）。联机游戏/P2P 场景才用得上；GUI 可放 Tools。 |
| `SubscribeServiceStatus` | 服务级状态（IDLE/STARTING/STARTED/STOPPING/FATAL + errorMessage）。carton 自己管理进程生命周期，用不上；若未来做"attach 到外部 sing-box 实例"则必接。 |
| `SubscribeOutbounds` | 全量 outbound 列表（含非组节点）。Proxies/Groups 页当前只消费组；若做"全部节点"视图再接。 |

## P4 —— 明确不接入（当前产品定位外）

| RPC / 能力 | 理由 |
| --- | --- |
| `SubscribeTailscaleStatus` / `SetTailscaleExitNode` / `StartTailscalePing` / `TailscaleLogout` / TailscaleSSH 全套 | 要求用户配置 Tailscale endpoint（`services.type: tailscale`）并登录 tailnet；与"代理客户端"定位正交，UI 面极大（设备列表/exit node/文件收发/SSH 终端）。真有需求时按官方 dashboard 的 TailscaleView 整页移植。 |
| Taildrop 文件收发全家（`SubscribeTaildropInbox`/`SendTaildropFiles`/`DownloadTaildropFile`…） | 同上；且需要 apiVersion ≥ 4（v1.14.0 的 `apiVersion` 是 3，**当前内核根本没有这些 RPC**——本地 proto 是 dev-next 快照，多出 taildrop/openconnect/openvpn，不可调用）。 |
| USB/IP（`ProvideUSBDevices`/`SubscribeUSBIPServerStatus`） | 远程 USB 共享，需要 `usbipd` 后端；纯极客场景。 |
| OpenConnect / OpenVPN 状态与挑战应答流 | 企业 VPN endpoint 管理，carton 的 profile 模型不支持这两种 endpoint 类型。 |
| `StartTailscaleSSHSession` 等双向流 | 需要内嵌终端 UI（xterm 级）；参考 dashboard TerminalView，工作量独立成篇。 |

## 版本兼容备忘

- carton proto 是 **dev-next 快照**，比 v1.14.0 多 taildrop/openconnect/openvpn RPC、少
  `GetTailscaleCertificate`/`SubscribeNotifications`；`TailscaleEndpointStatus` 字段编号有偏移。
  **carton 实际使用的消息与 v1.14.0 完全兼容**；接入任何 P3/P4 功能前，先把 proto 换成
  v1.14.0 release tag 版本并用 `GetVersion().apiVersion` 做运行时门控
  （官方：taildrop≥4，OpenVPN/OpenConnect≥3）。
- 内核版本策略：**不支持 <1.14.0，无兼容路径**。`KernelVersionGuard`（≥1.14.0）三层硬拦截：
  ① 启动时探测拒绝 + 阻塞弹框；② 在线下载按版本号预拦（不浪费流量）；③ 自定义安装先探测源二进制再拦截。
