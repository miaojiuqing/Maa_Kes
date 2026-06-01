<!-- markdownlint-disable MD033 MD041 MD022 MD032 MD003 MD026 MD009 -->

> **⚠️ 内测阶段 - 功能尚不完善，正在持续开发中！**

<p align="center">
  <img alt="LOGO" src="assets/resource/image/logo.png" width="256" height="256" />
</p>

<div align="center">

# Maa_KES

</div>

<div align="center">

基于 MaaFramework 的卡厄思梦境自动化助手。图像技术 + 模拟控制，解放双手！由 [MaaFramework](https://github.com/MaaXYZ/MaaFramework) 强力驱动！

[点击链接加入群聊【Maa_Kes交流群】](https://qm.qq.com/q/QbBRzgyUU2) — 群号：437587416

</div>

<p align="center">
  <img alt="Python" src="https://img.shields.io/badge/Python-3776AB?logo=python&logoColor=white">
  <img alt="license" src="https://img.shields.io/github/license/miaojiuqing/Maa_KES">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%7CAndroid-blueviolet">
  <img alt="commit" src="https://img.shields.io/github/commit-activity/m/miaojiuqing/Maa_KES">
  <img alt="stars" src="https://img.shields.io/github/stars/miaojiuqing/Maa_KES">
</p>

---

## 简介

**Maa_KES** 是由 miaojiuqing（淼九清）开发的游戏自动化工具，基于 MaaFramework 框架，致力于帮助玩家完成每日任务、体力清理、奖励领取等重复性操作。

> **MaaFramework** 是基于图像识别技术、运用 [MAA](https://github.com/MaaAssistantArknights/MaaAssistantArknights) 开发经验去芜存菁、完全重写的新一代自动化黑盒测试框架。
> 低代码的同时仍拥有高扩展性，旨在打造一款丰富、领先、且实用的开源库，助力开发者轻松编写出更好的黑盒测试程序，并推广普及。

**注意：** 本项目推荐使用 MuMu 模拟器、雷电模拟器，PC端也同步适配，请自行设置为 16:9 分辨率。

---

## 主要功能

### 启动
- ✅ 启动游戏并进入首页
- ✅ 支持模拟器自启动游戏

### 日常
- ✅ **喝咖啡** — 一键完成咖啡店日常
- ✅ **清体力** — 超时空流域刷取
  - ✅ 成长材料（单元币/主战员升级材料/辅战员升级材料）
  - ✅ 主战员（前锋/守卫/游侠/猎人/奥术师/操控师）
  - ✅ 辅战员（前锋/守卫/游侠/猎人/奥术师/操控师）
  - ✅ 潜能（热情/秩序/本能/虚无/正义）
  - ✅ 战斗效率选择（1~5次）
  - ✅ 挑战直到体力耗尽
- ✅ **政策审阅**
  - ✅ 审批奖励
  - ✅ 灾后重建方案（可自定义奖励优先级）
- ✅ **领奖**
  - ✅ 日常活跃奖励
  - ✅ 阿基亚农补给

### 工具
- ✅ **剧情跳过** — 自动跳过对话，支持跳过确认弹窗
- ✅ **自动推图** — 自动推进主线/战斗关卡
- ✅ **教学红点清除** — 自动清理教学界面的红点提示

---

## 使用说明

### ADB（模拟器）

软件启动后左上角资源选择「安卓端」，模拟器分辨率设置为 16:9，可挂后台运行。

### Win32（桌面端）

- 软件以**管理员方式**启动
- 左上角资源选择「桌面端」
- 游戏内窗口设置为 16:9 分辨率
- 鼠标/键盘输入建议选择 `PostMessageWithCursorPos` 或 `SendMessageWithCursorPos`，支持后台点击

---

## 常见问题

**Q: 软件启动后显示"资源加载失败"怎么办？**

A: 打开安装目录，删除除了 config 之外的文件夹，然后下载最新版解压到安装目录，重新启动即可。

> [!CAUTION]
> **Q: PC端连接不上窗口怎么办？**
> 
> A: 请用管理员身份运行，若以管理员方式运行后仍无法正常连接窗口，请在「运行设置 - 截图方式」中更改截图方式。

**Q: 提示"应用程序错误"怎么办？**

A: 一般是缺少运行库，请安装 [vc_redist](https://aka.ms/vs/17/release/vc_redist.x64.exe)。

**Q: 如何导出日志？**

A: 软件内点击「|→」即可导出日志。若 Bug 可稳定复现，建议导出前删除 debug 文件夹后再次触发 Bug。
---

## 免责声明

本软件开源、免费，仅供学习交流使用。若您遇到商家使用本软件进行代练并收费，产生的费用、问题及后果与本项目无关。

因 Maa_KES 自身漏洞、文本理解有歧义、异常操作导致的账号问题等开发组不承担任何责任，请在使用前仔细阅读说明，谨慎使用！

---

## 常用工具

1. [MaaDebugger](https://github.com/MaaXYZ/MaaDebugger) — 调试 pipeline JSON 节点
2. [MFATools](https://github.com/SweetSmellFox/MFATools) — 截图、取色、取区域

## 相关项目

1. [MaaFramework](https://github.com/MaaXYZ/MaaFramework) — 自动化黑盒测试框架
2. [MaaPracticeBoilerplate](https://github.com/MaaXYZ/MaaPracticeBoilerplate) — MaaFramework 项目模板
3. [MFAAvalonia](https://github.com/SweetSmellFox/MFAAvalonia) — 社区 GUI

## 鸣谢

本项目由 **[MaaFramework](https://github.com/MaaXYZ/MaaFramework)** 强力驱动！

感谢以下开发者对本项目作出的贡献：

<a href="https://github.com/miaojiuqing/Maa_KES/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=miaojiuqing/Maa_KES&max=1000" alt="Contributors"/>
</a>

## Star 历史

[![Star History Chart](https://api.star-history.com/svg?repos=miaojiuqing/Maa_KES&type=date)](https://www.star-history.com/#miaojiuqing/Maa_KES&type=date)
