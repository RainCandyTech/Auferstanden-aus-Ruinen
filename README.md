# AaR

**Auferstanden aus Ruinen**

在 NapCat 与 SnowLuma 相继停止维护之后，QQ NT Linux 生态需要一个继续存在的 OneBot v11 协议端。
AaR 从废墟中站起，接过了这个位置。

---

### V1.0.0 Preview 1 即将发布，敬请期待！

V1.0.0 Preview 1 里程碑：

|里程碑|达成版本/目标版本|
|--|--|
|OnebotV11的标准符合性|V0.5.2 OK|
|消息、seq、媒体索引的持久化|V0.5.0 OK|
|多账号以及在反向WS中的多账号正确实现|~~V0.8.0~~  V0.8.1 OK|
|Native桥接器基线版本|V1.0.0-preview1|

更多里程碑：

|里程碑|达成版本/目标版本|
|--|--|
|Onebot V12协议|V1.1.0-preview1？|
|Milky协议|V1.1.0-preview1？|

---

已知重大问题（并且可能不会修复）：

|问题|状态|缓解措施|
|--|--|--|
|在正向WS+多QQ连接到一个AaR主控的情况下，上报ID只会回退到一个QQ，发送消息同理|不会修复|请运行多个AaR主控|
|CQ码的解析可能出现问题|不会修复|请避免使用CQ码|

---

## 仓库结构

| 仓库 | 内容 | 可见性 |
|---|---|---|
| **AaR-Public**（本仓库） | AaR.Server 全部源码；Release 二进制经 GitHub Releases 分发 | 公开 |
| **AaR-RPCBridge** | 桥接模块源码（JS 桥 + Native Hook 桥）、构建与部署脚本 | 私有 |
| **AaR-Test** | 测试代码、协议验证数据与测试报告 | 私有 |

Release 产物（服务端 .NET 构建 + JS 桥 + Native 桥）不上传到 git，
统一发布在 GitHub Releases 页面。

---

## 这是什么

AaR 是面向 **QQ NT Linux 客户端（3.2.x）** 的 OneBot v11 协议端实现，由服务端与桥接模块组成：

| 组件 | 形态 | 职责 |
|---|---|---|
| **AaR.Server** | .NET 二进制 | OneBot v11 协议端本体：全部协议逻辑、传输通道、事件转换、对外接口 |
| **JS 桥** | JavaScript（**0.4.1 起明文分发**） | 运行于 QQ NT 主进程内部，**仅**将 RPC 调用转换为客户端内核函数调用、推送内核事件。不含任何协议逻辑 |
| **Native 桥** ⚠️ 预研中 | .NET host + 进程内原语（L4 契约） | 零 JS 依赖的桥接形态：契约规范见 `docs/l4-contract-v1.md`（core-v1 + 扩展组 + 多账号槽位），host 骨架与发送原语预研推进中。**Native 发送路径待验证**——生产环境以 JS 桥为准 |

协议端与桥接模块之间通过 SignalR 双向 RPC 通信。桥接模块对协议一无所知——
这一隔离是刻意设计的。JS 桥自 0.4.1 起以明文分发（此前为混淆分发）；
两种桥接形态与 AaR.Server 的契约完全一致，同机二选一运行。

## 运行环境

- Linux x64（Debian/Ubuntu 系实测）
- QQ NT 3.2.x（官方 deb）
- .NET 11 运行时（`dotnet-runtime-11.0`）
- 一个或多个已登录的 QQ NT 会话（**多账号 = QQ 多开**，每账号一个 QQ 进程——见下文「多账号」）

## 多账号（v0.8.x）

QQ NT Linux 的多账号形态是**多进程**（每账号一个 QQ 进程，各持自己的 NT
会话）。AaR 对此的完整语义：

1. **桥自动上线**：QQ 多开后，每个进程内的桥实例自动捕获自己的会话并
   以独立连接上线，服务端按账号（hello uin）分槽——无需任何多账号配置；
2. **`self_id` 路由**：OB11 调用（HTTP/WS）带 `self_id` 参数即以该账号
   执行（`send_group_msg`、`get_login_info` 等）；**缺省 = 主槽**（首个
   绑定的账号），单账号用户零感知；
3. **事件正身**：每个账号的事件以该账号的 `self_id` 上报（多账号同群
   收发时两条事件各盖各的身份）；
4. **反向 WS 一 bot 一连接**（v0.8.1）：`Reverse.WebSockets` 的每个目标
   可用 `"selfId"` 绑定账号——该连接只收该账号的事件，其回传的 API
   调用默认路由到绑定账号。未绑定的目标保持全量推送（兼容行为）；
5. **正向 WS 多 bot**：不做专门处理——事件已带 `self_id`，客户端按
   `self_id` 自行分流。

## OneBot v11 接口面

### 传输通道

| 通道 | 方向 | 说明 |
|---|---|---|
| 正向 HTTP | 框架 → AaR | `POST /api/{action}`，兼容 `POST /{action}` 与 GET 查询串 |
| 正向 WebSocket | 框架 ↔ AaR | `/ws`，事件直推 + 同连接 API 调用，连接即补发 `lifecycle connect` |
| 反向 WebSocket | AaR → 框架 | AaR 作为 WS 客户端连出，携带 `X-Self-ID` / `X-Client-Role` 头，同连接双向；v0.8.1 起目标可用 `selfId` 绑定账号（一 bot 一连接，事件过滤 + 回传 API 按绑定账号路由） |
| 反向 HTTP POST | AaR → 框架 | 事件上报（`X-Self-ID` 头），上游 2xx 响应体可携带 API 调用数组，AaR 执行 |

### 已实现 action

```
get_login_info  send_private_msg  send_group_msg  send_msg
delete_msg      get_msg           mark_msg_as_read
get_friend_list get_stranger_info get_recent_contact
get_group_list  get_group_info    get_group_member_list  get_group_member_info
set_group_kick  set_group_ban     set_group_whole_ban    set_group_card
set_group_special_title          set_group_name         set_group_leave
set_group_admin                 set_friend_add_request get_group_at_all_remain
send_poke*      set_msg_emoji_like
get_group_msg_history           get_friend_msg_history
get_forward_msg send_group_forward_msg   send_private_forward_msg
get_essence_msg_list            set_essence_msg        del_essence_msg
get_group_shut_list             send_group_notice
download_file   send_input_status
get_image       get_record        get_file
can_send_image  can_send_record   get_status  get_version_info
```

`*` 实验性透传，受内核路径限制（完整实现需要协议栈，见下文排除声明）。

### 消息段

收发双向：`text / at / face / reply / image / record / video / file / json`；
接收侧额外输出 `mface`（商城表情）。`message` 参数接受数组与 CQ 码字符串两种格式。
合并转发支持引用已有消息的 `node` 段（自定义内容节点暂未实现）。

`markdown` **仅收不发**：NT 内核只允许白名单官方 bot 账号发送 markdownElement，
发送方向返回 retcode 1200 显式错误。

媒体段（image/record/video/file）发送前由内核落盘到 QQ 自身媒体库
（`getRichMediaFilePathForGuild`，等价 NapCat uploadFile）；外部路径直发会被
NT 判「文件被修改，请稍后再试」。`get_image` / `get_record` / `get_file`
基于会话媒体索引，本地缓存未命中时经内核 `downloadRichMedia` 按需下载。

### 事件

`message` / `message_sent`（自发回执）/ `notice`（撤回、群成员增减、禁言、
群文件上传、poke）/ `request`（好友申请）/ `meta_event`（lifecycle、heartbeat）。
事件中的 id 类字段（`message_id` 等）为 NT 雪花 ID 字符串。

## 配置

AaR.Server 读取同目录 `appsettings.json` 的 `AaR` 段：

```jsonc
{
  "AaR": {
    "AccessToken": "",              // 留空 = 不鉴权；建议公网部署前设置
    "BridgeHubPath": "/hub/aar",    // 桥接模块连接端点
    "Ob11WsPath": "/ws",            // 正向 WS 端点
    "BridgeTimeoutMs": 15000,       // 内核 RPC 超时
    "HeartbeatIntervalMs": 30000,   // 心跳间隔
    "RequireBridgeForHttp": true,   // 桥接未连接时 HTTP API 返回 503（不再是无用的死配置）
    "EnableDebugCommand": true,     // #AaR 聊天调试命令（见下文）；设为 false 可关闭
    "EnableDebugKernel": false,     // debug_kernel 诊断透传（默认关闭）
    "Storage": {                    // 消息/媒体/uid 索引持久化（v0.5.0 起）
      "Provider": "sqlite",
      "Path": "data/aar.db"
    },
    "Reverse": {
      "WebSockets": [               // 反向 WS 目标（框架侧 WS 服务地址）
        // v0.8.1：多 bot 一 bot 一连接——selfId 绑定账号后该连接只收该账号
        // 事件、回传 API 默认路由绑定账号；省略 selfId = 全量（兼容）
        { "Url": "ws://127.0.0.1:8080/onebot/v11/ws", "AccessToken": "", "SelfId": "3774879303" },
        { "Url": "ws://127.0.0.1:8081/onebot/v11/ws", "AccessToken": "", "SelfId": "2074696141" }
      ],
      "HttpPost": [                 // 反向 HTTP 上报目标
        { "Url": "http://127.0.0.1:8080/onebot/v11/report", "AccessToken": "" }
      ]
    }
  }
}
```

日志与运行数据写在同目录。桥接模块侧有独立的 `aar-config.json`
（服务器地址与令牌），与 AaR.Server 的 `AccessToken` 保持一致。

## 聊天调试命令 `#AaR`

任何私聊/群聊中发送内容恰为 `#AaR` 的消息（`AaR:EnableDebugCommand`，
默认开启，设为 `false` 关闭），协议端会直接回复一条**脱敏**运行摘要：

- 版本、运行时长、桥接连接状态、事件收发计数、WS 客户端数
- 会话索引规模、鉴权令牌是否配置、来源（群聊/私聊）
- 所有 id 均掩码显示（如 `****7890`），不包含任何 uid/uin/URL/令牌

同时完整（未脱敏）诊断信息——运行时、内存、进程、自我身份、端点配置、
反向目标、索引明细、**多账号槽位清单（v0.8.1 起的 `slots:` 行）**——只写入
服务端日志（`#AaR debug command` 条目），聊天侧不可见。

- 同一会话 5 秒内重复触发会被静默忽略（防刷屏/防回环）
- 触发消息本身照常向框架广播，机器人自己的消息不会触发

## 关于部署

桥接模块需要在 QQ NT 的资源目录中获得一席之地，并让客户端的主入口认识它。
QQ NT 的入口解析链路（`package.json` 主字段、`app_launcher` 与内嵌虚拟文件系统
之间的关系）值得自行研究——提示到此为止，剩下的动手部分属于使用者的功课。

JS 桥具备自动重连与内核会话自愈能力；QQ 重启、登录状态变化、AaR.Server
重启均不需要人工干预。

Native Hook 桥（实验性）以 `LD_PRELOAD` 方式注入 QQ 主进程，与 JS 桥二选一
运行；详见 Releases 页面的版本说明。

## 兼容性备忘

- `message_id` 等标识字段为 NT 雪花 ID **字符串**（超过 2^53，JS 框架注意精度）
- **多账号**：同一物理消息在不同账号的事件里各有自己的 `message_id`（各账号
  内核独立发号）；`get_msg` 等「只凭 message_id 反查」的调用当前走主槽路由，
  跨账号 message_id 场景请带 `self_id`（v0.8.x 已知边界，后续版本对齐）
- 群聊 action（群列表/成员/管理/历史）已在真实群环境实测通过
- 图片/语音/视频/文件发送经内核媒体库落盘后在真实环境验证送达；NT v2 图片直链
  需要 packet 栈获取 rkey，当前以 `get_image` 本地缓存兜底
- 接收侧 `reply` 段的 `id` 经本地 seq 索引重写，可直接用于 `get_msg`
- `send_poke` 为实验性透传
- 事件与响应中的 id 类字段统一为字符串

## 金钱功能排除声明

AaR **不实现、不计划实现**任何涉及金钱的接口与消息元素：

- QQ 钱包、红包、转账、支付相关 API
- `WALLET` 消息元素（内部类型 9）
- 群文件上传（highway 计费通道）
- 会员、成长值、充值、打赏相关接口

以上为结构性排除，不接受相关功能请求。

## 致谢

- [NapCat](https://github.com/NapNeko) — 内核调用签名研究的主要公开参考
- [LLOneBot](https://github.com/LLOneBot) — 同上
- OneBot 11 标准的全体维护者

本项目与腾讯官方无关，使用官方客户端的内部接口，风险自负。

---

## 讨论区

offtopic讨论区：1136101939

其他话题请找管理。

---

## 许可证

- **源码**（本仓库 AaR.Server 目录）：AGPL-3.0，见 [LICENSE](LICENSE)
- **Release 二进制**（GitHub Releases 分发的构建产物与桥接模块）：CC BY-NC-ND 4.0
  （署名-非商业性使用-禁止演绎），见 [LICENSE-RELEASE](LICENSE-RELEASE)

使用即表示接受上述许可条款。
