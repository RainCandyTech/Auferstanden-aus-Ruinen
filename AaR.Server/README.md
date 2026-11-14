# AaR Server — AaR.Server

Auferstanden aus Ruinen — ASP.NET Core 侧。

**定位**：OneBot v11 协议端。对接 QQ NT 主进程内的 JS 桥（SignalR），对外提供
完整 OneBot v11 接口（HTTP + 正向 WebSocket），协议逻辑全部在本仓库。

## v0.3.0 修复与补全（2026-11-14）

- **媒体段发送彻底修复**：发送 image/record/video/file 前先经内核
  `getRichMediaFilePathForGuild` 落盘到 QQ 自身媒体库再发送（等价 NapCat uploadFile）。
  此前外部路径直发会被 NT 判「文件被修改，请稍后再试」（delivered:false）。
- **群聊内核事件缺失修复**：桥此前只注册了消息监听器，群列表/群成员事件
  （onGroupListUpdate / onMemberInfoChange 等）永远收不到 —— 现补注册
  `addKernelGroupListener` / `addKernelProfileListener`。get_group_list、
  get_group_info、get_group_member_list、get_group_member_info 全部恢复真实数据。
- **新增 action**：`set_group_admin`、`get_record`、`get_file`；
  `get_image` 由占位实现升级为「媒体索引 + 内核按需下载」。
- **新增事件**：`notice.group_upload`；`group_decrease`（leave/kick/kick_me，
  此前退群被错报为 group_increase+leave）。
- **历史消息重写**：`message_seq=0` 现在返回最新窗口（时间升序）；内核分页拉取
  规避 NT `getMsgsBySeqAndCount` 大 count 阻塞；响应瘦身避开 SignalR 32KB 上限
  （该上限是群历史超时的根因之一，服务端同时放宽到 4MB）。
- **接收映射修复**：`at.qq`（atUid/atNtUid 语义）、`reply.id`（经 replayMsgSeq
  反查本地索引，可被 get_msg 直接解析）、图片 url 回退 `gchatpic_new/<MD5>`、
  新增 `mface`（商城表情）段。
- **markdown 明确拒绝**：NT 内核只允许白名单官方 bot 账号发 markdownElement，
  发送方向现在返回 retcode 1200 显式错误而非静默丢内容。
- `send_msg` 支持「仅 group_id」自动判型；发送响应带内核 `result/errMsg` 便于诊断。

## 运行

```bash
cd AaR.Server
dotnet run
# 默认监听 http://127.0.0.1:5600（appsettings.json Kestrel 段）
```

配置（`appsettings.json` → `AaR` 段）：

| 键 | 说明 |
|---|---|
| `AccessToken` | 访问令牌（HTTP Bearer/query `access_token`、WS 同理；为空则不鉴权） |
| `BridgeHubPath` | SignalR hub 路径（JS 桥连接用），默认 `/hub/aar` |
| `Ob11WsPath` | OneBot 正向 WS 路径，默认 `/ws` |
| `BridgeTimeoutMs` | 桥 RPC 超时 |
| `HeartbeatIntervalMs` | WS 心跳间隔 |
| `EnableDebugKernel` | 开启 `debug_kernel` 诊断 action（原生内核方法透传，默认关闭） |

## OneBot 端点

- **HTTP**：`POST /api/{action}`、`POST /{action}`、`GET /api/{action}`，body 为 OB11 参数 JSON，
  响应 `{status, retcode, data, wording?, echo?}`
- **正向 WS**：`/ws`，帧为 `{"action","params","echo"}` → `{"status","retcode","data","echo"}`，
  事件（message / message_sent / notice / request / meta_event）无 echo 直推
- **桥连接**：JS 通过 SignalR 连 `/hub/aar`（`InternalCall` / `InternalCallResult` /
  `InternalEvent` / `InternalLog`）

## 传输

| 传输 | 方向 | 路径 |
|---|---|---|
| 正向 HTTP API | 框架 → 服务端 | `POST /api/{action}`、`POST /{action}`、`GET /api/{action}` |
| 正向 WebSocket | 框架 → 服务端 | `/ws`（事件直推 + API 请求） |
| **反向 WebSocket** | 服务端 → 框架 | `AaR:Reverse:WebSockets[]`，服务端作为 WS 客户端连出，事件推送 + 同连接 API 调用 |
| **反向 HTTP POST** | 服务端 → 框架 | `AaR:Reverse:HttpPost[]`，事件 POST 上报（带 `X-Self-ID`/`X-Client-Role` 头），**上游 2xx 响应体可携带 API 调用数组**，服务端执行 |
| SignalR 桥 | JS → 服务端 | `/hub/aar`（内部 RPC，无协议逻辑） |

反向通道配置示例：
```json
"Reverse": {
  "WebSockets": [ { "Url": "ws://127.0.0.1:8080/onebot/v11/ws", "AccessToken": "" } ],
  "HttpPost":   [ { "Url": "http://127.0.0.1:8080/onebot/v11/report", "AccessToken": "" } ]
}
```

## 已实现 action

```
get_login_info  send_private_msg  send_group_msg  send_msg
delete_msg      get_msg           mark_msg_as_read
get_friend_list get_stranger_info get_recent_contact
get_group_list  get_group_info    get_group_member_list  get_group_member_info
set_group_kick  set_group_ban     set_group_whole_ban    set_group_card
set_group_special_title          set_group_name         set_group_leave
set_group_admin                  set_friend_add_request get_group_at_all_remain
send_poke*      set_msg_emoji_like
get_group_msg_history           get_friend_msg_history
get_forward_msg send_group_forward_msg   send_private_forward_msg
get_essence_msg_list            set_essence_msg        del_essence_msg
get_group_shut_list             send_group_notice
download_file   send_input_status
get_image       get_record        get_file
can_send_image  can_send_record   get_status  get_version_info
```

`*` `send_poke` 为实验性透传（内核路径限制，与 NapCat kernel 路径行为一致——完整 poke 需 packet 栈）。

未实现（v2 规划）：`set_group_add_request` / `get_group_notice`（需要 notify/公告列表内核路径）、
`get_cookies` / `get_csrf_token` / `get_credentials`（需 packet 栈取 rkey）、`set_restart` / `clean_cache`。

## 消息段

支持（收发双向）：`text / at / face / reply / image / record / video / file / json`，
CQ 码字符串自动解析。接收侧额外输出 `mface`（商城表情）。

- `markdown` **仅收不发**：NT 内核只允许白名单官方 bot 账号发送 markdownElement，
  发送方向会返回 retcode 1200 的显式错误，不会静默吞内容。
- `node / forward` 内联段不支持；合并转发请用 `send_*_forward_msg`（引用已有消息 id）。
- 媒体段（image/record/video/file）发送前会由内核落盘到 QQ 自身媒体库
  （`getRichMediaFilePathForGuild` + 拷贝，等价 NapCat `uploadFile`），
  外部路径直接发给 sendMsg 会被 NT 判「文件被修改，请稍后再试」。
- 图片接收 url：NT v2 下载链（appid 1406/1407）需要 packet 栈取 rkey，本实现回退到
  `gchat.qpic.cn/gchatpic_new/.../<MD5>` 直链。
- `get_image` / `get_record` / `get_file` 基于会话内媒体索引（收发均建索引），
  本地缓存未命中时通过内核 `downloadRichMedia` 按需下载。

## 事件

- `message` / `message_sent`（自发回执，sendType 1=普通 / 5=合并转发，去重处理）
- `notice`: `friend_recall` / `group_recall`（已去重）/ `group_increase` / `group_decrease`
  （leave / kick / kick_me）/ `group_ban` / `group_upload` / `poke`
- `request`: 好友申请
- `meta_event`: `lifecycle(connect)` / `heartbeat`

## 金钱功能排除声明（设计红线）

本服务端**不实现**任何涉及金钱的接口与消息元素：
QQ 钱包 / 红包 / 转账 / 支付相关 API、`WALLET` 元素（内部类型 9）永不构造、
群文件上传（highway 计费通道）不支持、会员/成长值/充值相关接口不存在。
以上为结构性排除，不接受 feature request。

## 仓库划分

- 本仓库公开发布。
- `aar-js` 仓库单独发布且**混淆**：其内无协议逻辑，只有内核函数转发。
