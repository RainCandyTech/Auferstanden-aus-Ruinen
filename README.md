# AaR

**Auferstanden aus Ruinen**

在 NapCat 与 SnowLuma 相继停止维护之后，QQ NT Linux 生态需要一个继续存在的 OneBot v11 协议端。
AaR 从废墟中站起，接过了这个位置。

---

## 这是什么

AaR 是面向 **QQ NT Linux 客户端（3.2.x）** 的 OneBot v11 协议端实现，由两部分组成：

| 组件 | 形态 | 职责 |
|---|---|---|
| **AaR.Server** | .NET 二进制 | OneBot v11 协议端本体：全部协议逻辑、传输通道、事件转换、对外接口 |
| **桥接模块** | 混淆 JavaScript | 运行于 QQ NT 主进程内部，**仅**将 RPC 调用转换为客户端内核函数调用、推送内核事件。不含任何协议逻辑 |

协议端与桥接模块之间通过 SignalR 双向 RPC 通信。桥接模块对协议一无所知——
这一隔离是刻意设计的，也是本项目选择混淆分发而非开源分发的部分原因。

## 运行环境

- Linux x64（Debian/Ubuntu 系实测）
- QQ NT 3.2.x（官方 deb）
- .NET 11 运行时（`dotnet-runtime-11.0`）
- 一个已登录的 QQ NT 会话

## OneBot v11 接口面

### 传输通道

| 通道 | 方向 | 说明 |
|---|---|---|
| 正向 HTTP | 框架 → AaR | `POST /api/{action}`，兼容 `POST /{action}` 与 GET 查询串 |
| 正向 WebSocket | 框架 ↔ AaR | `/ws`，事件直推 + 同连接 API 调用，连接即补发 `lifecycle connect` |
| 反向 WebSocket | AaR → 框架 | AaR 作为 WS 客户端连出，携带 `X-Self-ID` / `X-Client-Role` 头，同连接双向 |
| 反向 HTTP POST | AaR → 框架 | 事件上报（`X-Self-ID` 头），上游 2xx 响应体可携带 API 调用数组，AaR 执行 |

### 已实现 action

```
get_login_info  send_private_msg  send_group_msg  send_msg
delete_msg      get_msg           mark_msg_as_read
get_friend_list get_stranger_info get_recent_contact
get_group_list  get_group_info    get_group_member_list  get_group_member_info
set_group_kick  set_group_ban     set_group_whole_ban    set_group_card
set_group_special_title          set_group_name         set_group_leave
set_friend_add_request          get_group_at_all_remain
send_poke*      set_msg_emoji_like
get_group_msg_history           get_friend_msg_history
get_forward_msg send_group_forward_msg   send_private_forward_msg
get_essence_msg_list            set_essence_msg        del_essence_msg
get_group_shut_list             send_group_notice
download_file   send_input_status
can_send_image  can_send_record   get_status  get_version_info  get_image
```

`*` 实验性透传，受内核路径限制（完整实现需要协议栈，见下文排除声明）。

### 消息段

收发双向：`text / at / face / reply / image / record / video / file / json / markdown`。
`message` 参数接受数组与 CQ 码字符串两种格式。合并转发支持引用已有消息的
`node` 段（自定义内容节点暂未实现）。

### 事件

`message` / `message_sent`（自发回执）/ `notice`（撤回、群成员变动、禁言、poke）/
`request`（好友申请）/ `meta_event`（lifecycle、heartbeat）。
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
    "Reverse": {
      "WebSockets": [               // 反向 WS 目标（框架侧 WS 服务地址）
        { "Url": "ws://127.0.0.1:8080/onebot/v11/ws", "AccessToken": "" }
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

## 关于部署

桥接模块需要在 QQ NT 的资源目录中获得一席之地，并让客户端的主入口认识它。
QQ NT 的入口解析链路（`package.json` 主字段、`app_launcher` 与内嵌虚拟文件系统
之间的关系）值得自行研究——提示到此为止，剩下的动手部分属于使用者的功课。

桥接模块具备自动重连与内核会话自愈能力；QQ 重启、登录状态变化、AaR.Server
重启均不需要人工干预。

## 兼容性备忘

- `message_id` 等标识字段为 NT 雪花 ID **字符串**（超过 2^53，JS 框架注意精度）
- 群聊相关 action 已实现但当前开发环境无群实测
- 图片/语音/视频发送已实现文件落盘与消息构造；富媒体上传通道（highway）未接入，大文件场景受限
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

## 许可证

本项目以 **CC BY-NC-ND 4.0**（署名-非商业性使用-禁止演绎）发布。详见License.txt

使用即表示接受上述许可条款。
