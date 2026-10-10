# 7945HX / 蛟龙 16 Pro 2023 CPU 三档预设

版本自 `10.19.34` 起写入。源码：`CpuProfilePresets`（`JiaoLongConfig.cs`），前端重置：`CPU_PROFILE_DEFAULTS`。

| 档位 | CpuProfile | EC 模式 | 长期 W | 短期 W | 温度墙 ℃ | 频率 MHz | 推荐 CO |
|---|---|---|---|---|---|---|---|
| A 办公静音 | `saving` | Quiet | 45 | 60 | 90 | 4400 | −8 |
| B 游戏均衡 | `default` | Balance | 78 | 108 | 95 | 5150 | −12 |
| C 生产力 | `performance` | Performance | 90 | 115 | 96 | 5250 | −10 |

### 软调度 SoftSchedule（自 10.19.38，powercfg）

| 档位 | AC EPP | DC EPP | AC CPMIN% | DC CPMIN% | AC Boost | DC Boost |
|---|---|---|---|---|---|---|
| A 办公 | 55 | 80 | 5 | 0 | 1 开 | 1 开 |
| B 游戏 | 25 | 50 | 15 | 5 | 2 激进 | 1 开 |
| C 生产力 | 0 | 40 | 70 | 25 | 2 激进 | 1 开 |
| 安静日常 | 35 | 55 | 10 | 5 | 2 激进 | 1 开 |

切 EC 模式 / CPU 页「应用」/ 一键安静日常时写入**当前**电源方案；不整包导入第三方 `.pow`。

风扇绑定（`Fan.*ModeFanRpm`）：静音 2700 / 均衡 3500 / 性能 5800（性能模式 WMI 上限区间 5000–5800）。

## EC vs SMU（10.19.39）

- **功耗墙**：以 EC CustomMode（SPL / SPPT / 温度墙）+ 保活为准；非圆口 AC 不强制开门。
- **SMU**：优先 Curve Optimizer；PPT/STAPM 与 EC 双写易打架，勿同时猛调。
- **软调度**：powercfg EPP / CPMIN / Boost；开机与切模式走 SoftSchedule，不再裸 `EnableTurbo`。
- **安静日常**：瞬时功耗 + 软调度 + 不清用户 `Cpu.Default`；同模式再点不退出。
- **退出安静日常**：切换到其他性能模式、CPU 页「应用」/重置、或 **CPU 相关** `SetConfig`（会恢复 Active SoftSchedule）。改主题/OSD 等无关配置不退出。
- **竞态防护**：`NoteExpectedPerfMode` + `BeginSuppressModeBindings`；Detect 不会冲掉 QuietDaily。
- **前端**：`quiet-daily-changed` / `config-changed` 刷新横幅与 store。

## 设计依据（摘要）

- OEM EC 长期约 75–85W、短期约 105–120W；闭源默认 SPL 101 偏高，同机型双烤更常落在 75–80W。
- 温度墙 90–96℃：给风扇与 CO 留余量，避免长期顶 99–100℃。
- CO 为推荐值，需用户在 SMU 页确认稳定性后再长期启用。

## 行为

- `App.ApplyCpuProfileOnModeChange=true`：切换 EC 性能模式时同步对应 CPU 档并下发功耗/温度墙/频率。
- 升级到 10.19.34+：自动刷新 Saving/Default/Performance；Custom 保留。
- 导入第三方 `AppSettings.json`：刷新三档预设，原始 SPL/SPPT 写入 Custom（长期 >95W 钳制到 90W）。
