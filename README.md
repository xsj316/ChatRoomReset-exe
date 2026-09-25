---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: e9402fbbdd875a4359af4b0210d1baa1_4ba6c7f0b87c11f1b172525400248c00
    ReservedCode1: JgRVz7EKdv/KSKyo+mtl7QIvmLfqOnFtN8xqBLufhzD006FxszICfUmKsUHbutcb6bNTxfaxA5dHhR6rZVtlci6d6ZAa88weM5XXNg/RxhkppMpO3wqYr1IOs/vqGAQoha7Z0f+HAzpStVXp0AqS/iZmPPPbhPDE8EFF7wpRXeKQm2bxXml0jSraPiw=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: e9402fbbdd875a4359af4b0210d1baa1_4ba6c7f0b87c11f1b172525400248c00
    ReservedCode2: JgRVz7EKdv/KSKyo+mtl7QIvmLfqOnFtN8xqBLufhzD006FxszICfUmKsUHbutcb6bNTxfaxA5dHhR6rZVtlci6d6ZAa88weM5XXNg/RxhkppMpO3wqYr1IOs/vqGAQoha7Z0f+HAzpStVXp0AqS/iZmPPPbhPDE8EFF7wpRXeKQm2bxXml0jSraPiw=
---

# ChatRoomReset Client（Chat Room 重置版客户端）

基于 WinUI 3 的 Windows 聊天室客户端，配套 Node.js 服务端（ChatRoomReset-sever）使用。采用微信风格三栏布局，支持邮箱验证注册登录、好友私聊、群聊、大附件分片传输、积分签到 / 红包 / 兑换码以及管理员兑换码生成。

## 技术栈

| 组件 | 说明 |
| --- | --- |
| 框架 | WinUI 3（Windows App SDK） |
| 目标框架 | net8.0-windows10.0.19041.0（`UseWinUI`） |
| Windows App SDK | 1.8.260804001 |
| SocketIOClient | 3.1.2（Socket.IO V4 客户端） |
| 语言 | C# / XAML |

## 功能

- **服务器连接**：启动页输入服务器地址并连通性检测，连接成功后进入登录页
- **账号体系**：用户名 + 邮箱 + 密码注册，邮箱验证码确认后账号方可登录；验证码可重发；用户名或邮箱登录；忘记密码（邮箱验证码重置）；登录态修改密码；退出登录
- **主页（Home）**：展示当前用户信息（头像 / 用户名 / 积分 / 角色）、每日签到（+10 积分）、积分流水、兑换码兑换；管理员额外拥有兑换码生成面板
- **好友**：按用户名搜索并添加好友、处理好友申请、好友列表（在线状态实时刷新）、点击好友进入私聊
- **私聊**：文本消息、发送文件（1MB 分片上传，服务端合并）、收到文件一键下载、发送积分红包、删除好友、拉黑
- **群聊**：创建群聊（生成群号 / 邀请码）、凭群号加入群聊、我的群列表、群消息与文件发送、右侧群友面板（成员头像 / 在线状态 / 群内角色，点击头像可发起好友申请）
- **设置**：切换服务器地址并重连、浅色 / 深色主题切换、兑换码兑换积分、查看当前账号、退出登录
- **本地缓存**：群聊列表使用 localStorage 持久化（服务端不保存群加入历史），点击列表项可凭群号重新加入

## 目录结构

```
client/ChatRoomReset/
├── ChatRoomReset.csproj    # 项目文件（net8.0-windows10.0.19041.0 + WinUI）
├── App.xaml / App.xaml.cs  # 应用入口与全局 Socket 实例
├── MainWindow.xaml(.cs)    # 主窗口与页面导航框架
├── Models/Models.cs        # 用户 / 好友 / 群 / 成员 / 消息 / 文件模型
├── Services/
│   ├── ApiService.cs       # REST API 封装（BaseUrl + Bearer token）
│   ├── SocketService.cs    # Socket.IO 实时通信（私聊 / 群聊 / 好友状态）
│   ├── FileTransferService.cs  # 1MB 分片上传、下载（保存至 Downloads/ChatRoomFiles）
│   └── SettingsService.cs  # 本地设置（服务器地址、token、用户信息、主题）
├── Converters.cs           # XAML 值转换器
└── Pages/
    ├── InitPage.xaml(.cs)      # 服务器连接页
    ├── LoginPage.xaml(.cs)     # 登录 / 忘记密码
    ├── RegisterPage.xaml(.cs)  # 注册 + 邮箱验证
    ├── HomePage.xaml(.cs)      # 主页（签到 / 积分 / 兑换码 / 管理员面板）
    ├── FriendsPage.xaml(.cs)   # 好友列表 / 搜索添加 / 申请处理
    ├── GroupsPage.xaml(.cs)    # 群列表 / 创建群 / 加入群
    ├── ChatPage.xaml(.cs)      # 私聊 / 群聊 / 文件传输 / 群友面板 / 红包
    └── SettingsPage.xaml(.cs)  # 设置（服务器 / 主题 / 兑换 / 退出）
```

## 构建运行

环境要求：Windows 10 19041 及以上，安装 [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) 与 [Windows App SDK 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/) 运行库。

```bash
cd client/ChatRoomReset
dotnet restore   # 还原 NuGet 依赖
dotnet build -c Release
dotnet run       # 或直接运行 bin/Release 下的可执行文件
```

构建产物为 WinUI 3 桌面应用（可执行文件 + 依赖目录），可通过 `dotnet publish` 打包分发：

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## 使用说明

1. 启动应用，在连接页输入服务端地址（默认 `http://localhost:3000`）并点击连接
2. 未注册用户先注册：填写用户名 / 邮箱 / 密码，查收邮箱验证码完成验证后登录
3. 登录后进入主页，可签到、查看积分流水；管理员可生成兑换码
4. 在好友页搜索用户发送好友申请，对方同意后即可私聊
5. 在群聊页创建群（记下群号）或凭群号加入群聊
6. 聊天窗口支持发送文本、上传 / 下载文件、群聊查看群友面板、私聊发积分红包

## 相关仓库

- 服务端：[ChatRoomReset-sever](https://github.com/)（Node.js + Express + Socket.IO + SQLite）
*（内容由AI生成，仅供参考）*
