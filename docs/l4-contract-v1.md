# AaR 桥契约 L4v1（l4-contract-v1）

**版本**：v1 · 2026-11-20 首版成文
**性质**：AaR.Server ↔ 桥（今日 JS 桥 / 未来 native host）之间的**规范性契约**。本文档与服务端同库同演化：**改契约的 PR 必须同一 commit 改本文档**（评审期漂移直接可见）。
**实现基线**：服务端 0.6.0 / JS 桥 0.4.2（源级全量盘点 2026-11-20；盘点过程与设计依据见 AaR-Test 仓库《双桥并行-Stage0》系列）。
**阅读对象**：任何 L4 桥实现（JS / C++ / C# / Rust——本契约不含任何 JS 机制假设）、AaR.Server 维护者。

---

## 0. 范围与红线

- L4 = 服务端↔桥的消息契约。L5（OB11 对外）与 L3（内核触达技术）不在本文。
- **红线继承**：金钱相关能力（钱包/红包/转账/支付、群文件上传计费通道）结构性排除——本契约永不定义此类方法/扩展组。

## 1. 连接模型（多账号槽位）

### 1.1 一连接 = 一账号 = 一 NT 会话

```
QQ NT 进程（N 账号；NT 会话名参数化：nt_1 / nt_2 / …）
└── 桥进程（一个进程，任意实现技术）
    ├── 会话 nt_1 ── SignalR 连接 #1（短身份 slot=1；hello 后绑 uin=A）
    ├── 会话 nt_2 ── SignalR 连接 #2（短身份 slot=2；hello 后绑 uin=B）
    └── 运行时新账号登录 → 发现会话 nt_k → 动态升起连接 #k
            ↓ 4 信封（§2）
AaR.Server
└── 注册表：uin → connection 槽位表（+ legacy 单桥槽，见 1.3）
```

- **账号锚定在连接上，不进调用参数**：`InternalCall` 无账号字段——服务端选连接 = 选账号；桥侧每连接绑定自己的会话（JS 形态：`getNTWrapperSession('nt_k')`）。
- 硬理由：**peerUid 是账号相对的**（同一群号在两个账号下是两个不同会话上下文）——账号必须绑定在低于调用的层级，连接级是唯一干净位。

### 1.2 身份申报（两时点模型）

**时点一 · 连接瞬间**（短身份，握手携带，静态）：

| 字段 | query 通道 | 头通道（优先） | 语义 |
|---|---|---|---|
| 桥类型 | `aar_bridge` | `X-AaR-Bridge` | `js` \| `native` \| 任意实现自称 |
| 契约版本 | `aar_core` | `X-AaR-Core` | 缺省 `v1` |
| 实现版本 | `aar_version` | `X-AaR-Version` | 如 `0.4.2` |
| 账号实例序 | `aar_slot` | `X-AaR-Slot` | 桥侧账号实例（JS 形态对应 nt_k） |

- **双通道**：服务端读取顺序 = 请求头优先 → query 兜底。原因：浏览器形态 WebSocket API（`new WebSocket(url)`）**无自定义头参数**（WHATWG 定义）——Electron 40 全局 WebSocket 实测如此；query 是全客户端兜底，头是能力完备客户端（.NET host 等）的首选干净通道。
- 能力清单**刻意不上握手**：caps 依赖登录态，单一事实源 = hello 事件。

**时点二 · 登录 settle 后**（hello 事件，可重发）：

```json
InternalEvent("hello", {
  "bridge": "native",            // 同短身份，重发时可刷新
  "core":   "v1",
  "ext":    ["ext-media-v1", "ext-group-read-v1"],   // 能力申报，见 §3
  "uin":    "3774879303",        // 到达才绑定槽位
  "gen":    1763616000           // 该连接会话代际（epoch）
})
```

### 1.3 注册表策略

| 状态 | 语义 |
|---|---|
| **legacy 槽** | 零短身份连接（今日 JS 桥 0.4.x）= 单桥语义**原样保留**：新连踢旧、承担缺省路由、按满能力假定 |
| pending 槽 | 有短身份、无 uin：等待 hello 绑定；**按满能力假定**（守卫不生效） |
| bound 槽 | hello 携 uin 到达：进 `uin → connection` 槽位表 |
| 同 uin 重连 | 新连顶替旧连（旧连被踢 + InternalLog 通知）——重连语义逐槽保留 |
| 异 uin | 共存 |

- 未带短身份的连接发 hello：忽略（legacy 连接的身份只能来自握手）。
- 缺省路由：显式目标 uin → 对应槽；无目标 → legacy 槽优先，其次首个绑定的槽。**多账号时哪个账号当 OB11 缺省** = OB11 多 bot 服务形态课题，另行拍板，不属本契约。

## 2. 信封（SignalR 线面，共 4 条——本契约的全部线面）

| 方向 | 消息 | 形状 |
|---|---|---|
| 服务端→桥 | `InternalCall(callId, method, paramsJson)` | 泛型 RPC 信封（callId = GUID N） |
| 桥→服务端 | `InternalCallResult(callId, ok, dataJson, err)` | RPC 回执 |
| 桥→服务端 | `InternalEvent(name, payloadJson)` | 事件推送（含保留名 `hello`，§1.2） |
| 桥→服务端 | `InternalLog(level, line)` | 诊断日志（info/warn/error） |

**语义面全部在词汇层**（method 字符串 + name 字符串 + 各自 JSON 形状）。本契约永不增加第 5 条信封。

**超时包络**（事实契约，桥实现必须满足）：

| 场景 | 超时 |
|---|---|
| 默认 | `BridgeTimeoutMs`（服务端配置，现值 45000ms） |
| self.info | 10000ms |
| msg.send 含文件 | 180000ms |

**传输协议要素**：见附录 B（`aar-signalr.js` 手写客户端为参照实现）。

## 3. 能力模型（core-v1 + 扩展组）

### 3.1 core-v1（基线，充要——没有它不配称桥）

| 项 | 内容 |
|---|---|
| `self.info` / `uin.toUid` / `uid.toUin` | 身份与 id 解析 |
| `msg.send`（基线元素集：text/at/face/reply） | 文本发送是定义性能力 |
| `events.recv`（onRecvMsg）/ `events.update`（onMsgInfoListUpdate） | 事件接收；**delivered 确认依赖后者** |
| hello 协议 | 协商先于一切 |

### 3.2 扩展组注册表（10 组，38 契约方法全部分配）

| 扩展组 | 覆盖方法 |
|---|---|
| `ext-media-v1` | msg.prepareMedia / msg.downloadMedia + msg.send 的媒体元素集（pic/record/video/file） |
| `ext-msginfo-v1` | msg.recall / msg.getByMsgIds / msg.setRead / msg.emojiLike / msg.poke / msg.inputStatus |
| `ext-history-v1` | msg.historyBySeq |
| `ext-forward-v1` | msg.getForward / msg.sendForward |
| `ext-group-read-v1` | group.list / info / memberList / memberInfo / shutList / remainAtTimes / essence.list |
| `ext-group-admin-v1` | group.kick / ban / banMember / setCard / setRole / setSpecialTitle / setName / quit / essence.add / essence.remove / notice.publish |
| `ext-buddy-v1` | buddy.list / buddy.approveRequest + events.buddy（onBuddyReqChange） |
| `ext-user-v1` | user.detail |
| `ext-recent-v1` | recent.list |
| `ext-debug-v1` | debug.call |

### 3.3 演化规则

1. **扩展组 append-only 走版本后缀**：组内加方法 → `-v(N+1)`（客户端声明 v2 隐含 v1）；字段语义变更同款；
2. **core 变更 → L4 整体升版**（v2），不修订 v1；
3. 服务端按「动作 → 所需扩展（§6 分派表）」守卫，**只对 hello 申报过的槽生效**——legacy/pending 槽按满能力假定放行（过渡兼容）；
4. 守卫失败响应：`retcode 1200` + `wording: bridge capability missing: <ext>`。

### 3.4 跨组方法

`msg.send` 横跨基线与 `ext-media`：基线元素集（text/at/face/reply）→ core 检查；媒体元素 → 追加查 ext-media（按消息元素类型分派）。

## 4. 方法词汇（38 契约方法）

### 4.0 通用约定

- 一切 id 类字段为**字符串**（NT 雪花 ID 超 2^53，数字精度红线）；
- chatType：`1`=好友、`2`=群、`100`=群临时会话；群场景 peerUid = 群号字符串；
- 「**透传**」= 桥返回内核对象原样，服务端不依赖其深度形状（契约刻意薄）；
- 错误：`InternalCallResult` `ok=false` + err 字符串 → 服务端映射 OB11 retcode；
- 延迟包络见 §2。

### 4.1 core-v1

| 方法 | 参数 | 响应（服务端消费） |
|---|---|---|
| `self.info` | — | `{uid, uin, nick}` |
| `uin.toUid` | `{uin}` | `{uid}` |
| `uid.toUin` | `{uid}` | `{uin}` |
| `msg.send` | `{chatType, peerUid, elements[]}` | `{msgId, result, errMsg, delivered}` |

**msg.send 细则**：

- elements 由**服务端**（Ob11Mapper）构造为内核对象 JSON 形，桥**零翻译直传**（仅 NAPI Map 归一化）；
- `msgId` 为桥侧生成的发送标记（经 peer.guildId 携带），真实 msgId 由 `events.update` 学习（见 §5.2）；
- `delivered`：桥侧事件匹配（标记 + `sendStatus:2`，10s 窗）——**已知假阴性区间**（本机常态），服务端以历史回读为权威；
- reply 元素字段历史拼写 `replay*`（NT 的 bug）——两侧实现均**不纠正**。

### 4.2 ext-media-v1

| 方法 | 参数 | 响应 |
|---|---|---|
| `msg.prepareMedia` | `{srcPath, fileName?, elementType, elementSubType?, md5HexStr?, thumbPngBase64?}` | `{md5HexStr, fileName, path, fileSize, thumbPath?, thumbSize?, thumbMd5HexStr?}` |
| `msg.downloadMedia` | `{chatType, peerUid, msgId, elementId, filePath?, timeoutMs?}` | `{filePath}` |

**细则**：媒体发送必须先经 prepareMedia 落盘 QQ 自身媒体库（`getRichMediaFilePathForGuild`）——外部路径直发必败（NT 判「文件被修改，请稍后再试」）；视频缩略位 = `<Ori→Thumb>/<md5>_0.png`。

### 4.3 ext-msginfo-v1

| 方法 | 参数 | 响应 |
|---|---|---|
| `msg.recall` | `{chatType, peerUid, msgId}` | `{result, errMsg}` |
| `msg.getByMsgIds` | `{chatType, peerUid, msgIds[]}` | `{msgList: [MsgRecord]}` |
| `msg.setRead` | `{chatType, peerUid}` | 透传 |
| `msg.emojiLike` | `{chatType, peerUid, msgSeq, emojiId, set}` | 透传 |
| `msg.poke` | `{chatType, peerUid, targetUin(群)/targetUid(私)}` | 透传 |
| `msg.inputStatus` | `{chatType=1, peerUid, eventType}` | 透传 |

### 4.4 ext-history-v1

| 方法 | 参数 | 响应 |
|---|---|---|
| `msg.historyBySeq` | `{chatType, peerUid, msgSeq, count, desc}` | `{msgList: [MsgRecord-slim]}` |

**细则**：`count ≤ 5`/页（NT 大 count 阻塞 JS 线程）；**每页必须新建 peer 对象**（NAPI 接管纪律）；响应为 slim 白名单投影（§5.3）——「服务端消费字段」即契约。

### 4.5 ext-forward-v1

| 方法 | 参数 | 响应 |
|---|---|---|
| `msg.getForward` | `{chatType, peerUid, rootMsgId, parentMsgId?}` | `{msgList: [...]}`（全记录透传） |
| `msg.sendForward` | `{chatType, peerUid, peerUin, msgIds[]}` | 透传 |

### 4.6 ext-group-read-v1

| 方法 | 参数 | 响应 |
|---|---|---|
| `group.list` | `{forced?}` | `{groups: [...]}`（消费 groupCode/groupName） |
| `group.info` | `{groupCode}` | `{info: {groupCode, groupName, memberCount, maxMember}}` |
| `group.memberList` | `{groupCode, forced?}` | 成员数组（消费 uid/uin/nick/cardName/role/level） |
| `group.memberInfo` | `{groupCode, uid}` | 同上单条 |
| `group.shutList` | `{groupCode}` | 透传 |
| `group.remainAtTimes` | `{groupCode}` | 顶层或 `data.atInfo` |
| `group.essence.list` | `{groupCode}` | 透传 |

### 4.7 ext-group-admin-v1

| 方法 | 参数 | 响应 |
|---|---|---|
| `group.kick` | `{groupCode, uids[], refuseForever?, reason?}` | 透传 |
| `group.ban` | `{groupCode, shutUp}` | 透传 |
| `group.banMember` | `{groupCode, list: [{uid, timeStamp}]}` | 透传 |
| `group.setCard` | `{groupCode, uid, cardName}` | 透传 |
| `group.setRole` | `{groupCode, uid, role}`（3=admin, 2=member） | 透传 |
| `group.setSpecialTitle` | `{groupCode, uid, specialTitle}` | 透传 |
| `group.setName` | `{groupCode, groupName}` | 透传 |
| `group.quit` | `{groupCode, needDeleteLocalMsg?}` | 透传 |
| `group.essence.add` | `{groupCode, msgId}` | 透传 |
| `group.essence.remove` | `{groupCode, msgId}` | 透传 |
| `group.notice.publish` | `{groupCode, content, pinned?}` | 透传 |

### 4.8 ext-buddy / ext-user / ext-recent / ext-debug

| 方法 | 参数 | 响应 |
|---|---|---|
| `buddy.list`（ext-buddy-v1） | — | `{friends: [{uin, nick, remark}]}` |
| `buddy.approveRequest`（ext-buddy-v1） | `{friendUid, reqTime, accept}` | 透传 |
| `user.detail`（ext-user-v1） | `{uid}` | `{profile: {...}}`（服务端取昵称） |
| `recent.list`（ext-recent-v1） | — | 透传 |
| `debug.call`（ext-debug-v1） | `{method, params}` | 目标方法原始返回（仅门控调试通道） |

## 5. 事件词汇

### 5.1 onRecvMsg（core：`events.recv`）

payload = 监听器 args 元组：`[ [MsgRecord...] ]`。MsgRecord 契约字段（服务端消费口径）：

`chatType`（1/2）· `msgType`（5=纯灰条记录）· `msgId` · `msgSeq` · `msgTime` · `peerUid` · `senderUid` · `senderUin` · `sendType` · `sendNickName` · `sendMemberName` · `subMsgType` · `elements[]`

### 5.2 onMsgInfoListUpdate（core：`events.update`）

payload 同构。服务端消费：`chatType` · `msgId` · `msgSeq` · `peerUid` · `recallTime`（≠0 → 撤回通知）· `senderUid` · `senderUin` · `sendType`（1=文本/5=合并转发）· `sendStatus`（2/3 → message_sent）· `guildId`（**发送标记 → 真实 msgId 映射**，标记以 `7` 开头）· `elements[]`

**本事件入 core 的理由**：msg.send 的 delivered 确认与真实 msgId 学习都依赖它。

### 5.3 Element 形状（slim 白名单 = 契约）

每元素保留键：`elementType` · `elementId` · `textElement` · `faceElement` · `picElement` · `pttElement` · `videoElement` · `fileElement` · `arkElement` · `replyElement` · `grayTipElement` · `markdownElement` · `marketFaceElement`

- `markdown` **仅收不发**：NT 内核只允许白名单官方 bot 发送 markdownElement，发送方向失败是设计行为；
- reply 元素字段历史拼写 `replay*`（NT bug，不纠正）。

### 5.4 onBuddyReqChange（ext-buddy：`events.buddy`）

payload = `[ { reqList: [ { uid, friendUid, msg, reqTime } ] } ]`

### 5.5 转发未消费（6 件，附录 A）

onAddSendMsg / onSendMsgError / onBuddyListChange / onGroupListUpdate / onMemberInfoChange / onGroupSingleScreenNotifies——桥可转发，服务端 v1 未映射。

### 5.6 内部专用（不上行）

`cb:<prop>`：桥侧 invoke+等待模式（callWithEvent）的一次性等待器通道，永不发给服务端。

## 6. 分派表（OB11 action → core/ext）

**已验证映射**（守卫依据；`_async` / `_rate_limited` 后缀先剥离再查表）：

| 所需 | OB11 actions |
|---|---|
| core-v1 | send_msg / send_group_msg / send_private_msg / get_login_info（及其余未列出动作） |
| ext-forward | send_group_forward_msg / send_private_forward_msg / get_forward_msg |
| ext-msginfo | delete_msg / get_msg / mark_msg_as_read / set_msg_emoji_like / send_poke / group_poke / friend_poke / send_input_status |
| ext-history | get_group_msg_history / get_friend_msg_history |
| ext-buddy | get_friend_list / set_friend_add_request |
| ext-group-read | get_group_list / get_group_info / get_group_member_list / get_group_member_info / get_group_shut_list / get_group_at_all_remain / get_essence_msg_list |
| ext-group-admin | set_group_kick / set_group_ban / set_group_anonymous_ban / set_group_whole_ban / set_group_admin / set_group_card / set_group_special_title / set_group_name / set_group_leave / set_essence_msg / del_essence_msg / send_group_notice |
| ext-media | get_image / get_record / get_file / download_file |
| ext-user | get_stranger_info |
| ext-recent | get_recent_contact |
| ext-debug | debug_kernel |

- 未列出动作 = 服务端本地或未实现，不触发守卫；
- 发送动作按 core 放行；**媒体元素级的 ext-media 分派**由内核错误面自然承担（§3.4，后续可加元素级守卫）。

## 7. 附录

### 附录 A · 未契约清单（暂不使用——未来经扩展组版本升收编）

方法（5，debug.call 可达）：`msg.videoUrl` / `msg.historyAround` / `msg.generateUniqueId` / `buddy.isBuddy` / `user.detailByUin`
事件（6，§5.5）。

### 附录 B · 传输协议要素（参照实现：桥侧 aar-signalr.js 零依赖手写客户端）

```
POST {base}/hub/aar/negotiate?negotiateVersion=1    → connectionToken
WS   {base}/hub/aar?id=<token>&access_token=<token>（+ §1.2 短身份项）
握手  {"protocol":"json","version":1}\x1e           → 空响应
帧    type1 invoke {target, arguments}  |  type6 ping ↔  |  type7 close
分隔  RS = \x1e；keepAlive 15s；重连指数退避 1s→30s
鉴权  Authorization: Bearer / query access_token（WS 握手放不了头 → SignalR 惯例）
```

### 附录 C · 变更纪律

1. 本文档与实现同 PR（改形状必须同 commit 改本文）；
2. v1 内**只增不改**：新方法/新事件/字段新增 = 增补；重命名/删除/语义变更 = L4v2；
3. 每次服务端发版（Rv tag）在发布说明标注所实现的契约版本。

---

**文档版本历史**：v1（2026-11-20）首版：4 信封 + 多账号槽位模型 + core/扩展注册表 + 38 方法 + 5 事件 schema + 分派表 + 未契约清单。
