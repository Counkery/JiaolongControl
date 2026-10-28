# 项目体检报告 — 2026-10-28

> 对象：`remake/jiaolong-control` @ `04811f6` + 本次回灌的 8 项修复（未提交）
> 方法：编译（两个工程）＋ 类型检查 ＋ eslint ＋ 逐文件代码审查 ＋ 上游 release notes 交叉核对
> 结论：**构建与类型检查均通过（0 错误）**，但存在 3 个高优先级缺陷、7 个用户可感知缺陷、8 个健壮性/一致性问题。

---

## P1 — 高优先级（会造成严重后果）

### P1-1 自动更新指向**上游仓库**，会引导安装上游安装包覆盖本 fork

| 项 | 证据 |
| --- | --- |
| 更新源写死上游 | `Core/Utils/InnoUpdater.cs:13` `RepoApiUrl = "https://api.github.com/repos/GaoXanSheng/JiaolongControl/releases/latest"` |
| 每次启动无条件检查 | `App.xaml.cs:75-79` `Task.Run(() => new InnoUpdater(version).CheckForUpdatesAsync())` |
| 只认 `_Setup.exe` | `InnoUpdater.cs:48` `assetName.EndsWith("_Setup.exe")` |
| 上游确实有该资产 | GitHub API 实测：`10.20.08` 附带 `JiaoLongControl_10.20.08_Setup.exe`（67 MB） |
| 版本比较会成立 | `10.20.08` vs 本 fork `10.19.74` → `Version(10,20,8) > Version(10,19,74)` = true |

**后果**：每次启动弹「发现新版本 10.20.08」模态框；用户若点「立即更新」，会下载并运行**上游 Inno 安装包**，把 exe/dll 换成上游版本 —— 7945HX 三档预设、QuietDaily、CpuPowerKeepAlive、侧栏重组全部消失，且配置字段不兼容。用户点「跳过此版本」只能躲一次（写入 skipped 版本）。

**建议**：三选一
1. 改成 fork 自己的 releases，并只匹配 `*-win-x64.zip`（需先有正式 release 流程）；
2. 默认关闭自动检查，设置页提供「手动检查更新」；
3. 至少改为「只提示、不下载安装」。

---

### P1-2 崩溃/强杀后，厂家 OSD 进程可能被**永久挂起**

| 项 | 证据 |
| --- | --- |
| 用挂起进程的方式屏蔽厂家 OSD | `Native/NativeOsdSuppressor.cs:135-145` `NtSuspendProcess` |
| 只有优雅退出才恢复 | `:50-56` `Stop()` → `ResumeAllUtilities()`；`:83` 循环结束也恢复 |
| 启动时不做恢复 | `:42-48` `Start()` 只起循环，没有「先恢复历史挂起」的步骤 |
| 重复挂起会累加计数 | `NtSuspendProcess` 每次调用递增挂起计数；进程表 `SuspendedUtilityPids` 只在内存里，进程重启即为空 |

**后果**：程序被任务管理器强杀 / 崩溃后，`BLDFnHotkeyUtility.exe` 停在挂起态 —— 原厂 Fn/CapsLock 提示不再出现。下次启动又会**再挂起一次**（计数变 2），退出时只 `NtResumeProcess` 一次，仍解不开，只能重启系统。

**建议**：`Start()` 里先对所有 `BLDFnHotkeyUtility` 进程调一次 `NtResumeProcess` 做「清账」，再进入抑制循环；或把挂起次数持久化并等量恢复。

---

### P1-3 功耗保活每 30 秒拉起约 12 个 `powercfg` 子进程，且超时机制失效

| 项 | 证据 |
| --- | --- |
| 30s 周期 | `Services/CpuPowerKeepAliveService.cs:233` `WaitOne(Clamp(IntervalMs,5000,120000))`，默认 `IntervalMs=30000`（`:18`） |
| 每轮重写策略 | `:284` `ReapplyProcessorPolicy()` → `:325` `ApplySoftSchedule(...)` → `PowerController.cs:127-149` **11 条 powercfg 命令**，逐条 `RunPowerCfg` |
| 另有 NVML 写入 | `:337-344` 每轮 `SetCPUMaxFrequency` + `SetPowerLimit`；`:353-367` 每轮重发 GPU 锁频/偏移 |
| 超时是死代码 | `PowerController.cs:214-220` 先 `StandardOutput.ReadToEnd()` / `StandardError.ReadToEnd()`，**之后**才 `WaitForExit(timeout)` |

**后果**：
- 常驻后台每分钟 ~24 次进程创建（笔电续航/CPU 唤醒开销），任务管理器里会看到 powercfg 频繁闪现；
- `powercfg` 一旦卡住（电源服务异常），`ReadToEnd()` 会永久阻塞，超时判断永远到不了，保活线程就此挂死；
- 每 30s 重发 GPU 功耗墙/锁频，会与用户在 GPU 页做的曲线调校（undervolt）互相打架。

**建议**：`ReadToEnd` 改为 `BeginOutputReadLine` 异步读 + `WaitForExit(timeout)`；`ReapplyProcessorPolicy` 只在「值变化 / 从睡眠恢复 / 电源方案切换」时执行，而不是每轮无条件执行。

---

## P2 — 用户可直接感知的功能缺陷

### P2-1 Logo 灯开关：失败也提示成功、开关不回滚、初始状态取错

`Client/src/pages/Settings/components/LogoLight.vue`

- `:10` `logolight.value = (await LogoLight.Get()).Success` —— 取的是**读取调用是否成功**，不是灯的实际开关状态，所以首次进入状态随机。
- `:18` `if (result)` —— `result` 是 `CommandResult` 对象（`bridge.ts:499-502` 直接 `call(...)`），**恒为真**，`else` 分支永远不执行 → 无论硬件成功失败都弹「应用成功」。
- `:22` `logolight.value = result` —— 把对象塞进 `boolean` ref，失败时不会回滚。

**修复**：`if (result.Success) … else { Message.error(...); logolight.value = !logolight.value }`；初始值改用 `LogoLight.Get()` 返回的实际状态字段（若无该字段，改为读 `config.App.LogoLight`）。

### P2-2 风扇曲线切页会丢掉最后一次修改

`Client/src/composables/useFanCurveEditor.ts`

- `:150-161` 曲线变化 → 500ms 内部防抖 → `autoSave()` → `configStore.debouncedSave()`（`stores/config.ts:66` 又 1000ms）。
- `:260-263` `onUnmounted` 里 `clearTimeout(autoSaveTimer)` **不 flush**。

拖完一个点 0.5 秒内切页 → 定时器被清掉，改动既没进 config，也没触发保存。这与上游 release notes「风扇曲线保存去掉 500ms + 1000ms 双重防抖，切页时不再丢失最后一次修改」是同一个问题，本 fork 仍存在。

**修复**：卸载时若 `autoSaveTimer` 存在则立即 `autoSave()`；或去掉内层 500ms，直接调 `configStore.debouncedSave()`。

### P2-3 键盘渐变停止失败时仍显示「已停止」

`Client/src/pages/KeyBoard.vue:94-100`：先 `gradientRunning.value = false`，再判断 `res.Success`。失败时界面已翻转，且不回滚。

### P2-4 配置未加载完成时，依赖卡片会全部显示

`Client/src/pages/Settings.vue:82`
```ts
bootToggleCards.filter((card) => !card.visibleWhen || !config || card.visibleWhen(config))
```
`!config` 直接放行 → 首屏闪出一批依赖项（如「分核 Curve Optimizer」）再消失，用户可能误点。

### P2-5 配置拉取去重失效，启动时重复请求

`Client/src/stores/config.ts:28-31`
```ts
if (fetchPromise) { await fetchPromise.catch(() => {}) ; if (fetchPromise) return fetchPromise }
```
`finally` 里已把 `fetchPromise = null`，await 之后判断恒为 false → 直接落到下面再发一次 `GetConfig`。并发调用者越多，重复请求越多。

### P2-6 配置迁移「删除重建」实际没有重建

`Server/Core/Utils/ConfigSerializer.cs:117-131`
```csharp
var existing = Load();
if (string.IsNullOrWhiteSpace(existing.Version)) { File.Delete(ConfigPath); Save(new JiaoLongConfig{Version=version}); }
if (existing.Version != version) { Update(existing, version); }   // ← 用旧对象覆盖，重建白做
```
`Update()` 末尾 `Save(LowConfig)`（`:243-244`），于是刚重建的默认配置立刻被旧对象覆盖。注释写的行为与实际不符（上游 10.20.08 已删除该分支并注明"消除矛盾行为"）。

### P2-7 CPU 芯片 SVG 溢出 64×64 容器

`Client/src/components/common/CpuDie.vue:12-13` 固定 `width="200" height="200"`，而被放进 `w-16 h-16`（64×64）的容器：`CPU.vue:645`、`RyzenSmu.vue:537` → 图形溢出边框、压到右侧文字。上游已改为 `class="w-full h-full"` 并删掉固定宽高。

---

## P3 — 健壮性 / 一致性

| # | 问题 | 位置 | 说明 |
| --- | --- | --- | --- |
| P3-1 | `Stop()` 阻塞 UI 最长 3s，且 CTS 提前释放 | `CpuPowerKeepAliveService.cs:174-184` | `_task.Wait(3000)` 超时后仍 `_cts.Dispose()`；循环里 `token.WaitHandle.WaitOne` 可能抛 `ObjectDisposedException` |
| P3-2 | 遥测轮询无重入保护 | `stores/systemInfo.ts:158-163` | `setInterval` + 无 `if (loading) return`；硬件调用慢时会叠加请求 |
| P3-3 | `fetchStaticGpu` 并发重复 | `stores/systemInfo.ts:61-86` | `staticGpuLoaded` 只在末尾置位，`startPolling` 与 `fetchSystemInfo` 会各拉一次 |
| P3-4 | 前后端契约漂移 | `types/config.ts:105-106`、`utils/bridge.ts:508-509`、`:483` | `TdcLimitMp1/Rsmu` C# 已无对应字段；`Keyboard.GetMode/SetMode` C# 有但 UI 无用；`Fan.SetFanSpeed` 用 `toByte`（非整百会抛异常）且**无任何调用方** |
| P3-5 | 便携包实为 framework-dependent | `.github/workflows/release.yml:43`、`JiaoLongControl.csproj` | 无 `RuntimeIdentifier` / `SelfContained`，zip 解压后仍需 .NET 8 Desktop Runtime，README 未提及 |
| P3-6 | 个人路径硬编码进发布代码 | `Core/Utils/BatteryThresholdLauncher.cs:24` | `D:\Application\JiaoLong16Pro-Battery-threshold`，应删掉或改为相对路径 |
| P3-7 | 版本元数据不一致 | `README.md:15/25/102/118`、`Client/package.json` | README 写 10.19.50，csproj 为 10.19.74；package.json version 为 `0.0.0` |
| P3-8 | 23 条编译警告未清理 | `FanController.cs:127`（CS0108 隐藏基类成员）、`RyzenSmuController.cs:586`（`lhmEx` 未用）、`CommandResult.cs:24` 等 | CS0108 建议加 `new`，其余为 null 字面量与未用变量 |

另外：`.gitignore` 末尾有一行注释乱码（`# ���й�������`，编码损坏），不影响功能。

---

## 已确认「不需要再修」的项

避免与上游 release notes 混淆，以下问题**本 fork 已经处理**：

- 退出清理重复执行导致的 NRE —— `App.xaml.cs:100` 已用 `Interlocked.Exchange` 幂等。
- 模式切换失败后界面不回滚 —— `Home.vue:141-148` 失败时 `fetchPerformanceMode()` 回滚。
- CPU 页 PL1/PL2 标签写反 —— 本 fork 用「长期 SPL / 短期 SPPT」，且 `SetCpuLongPower/SetCpuShortPower` 映射正确。
- 模式切换一次点击向 EC 连写三次 —— `PerformanceModeController.Set` 只写一次，绑定经 `ApplyHardwareBindings` 单路下发。
- 上游本次修复的 8 项（WMI 电压、TDC/EDC、GPU 风扇 RPM、温度合法性、OpenUrl、配置 null 兜底、亲和性上限、曲线缓存）—— 已于本次回灌，见 `Doc/UPSTREAM_DIFF_10.20.08.md` 第 10 节。

---

## 建议处理顺序

1. **P1-1 更新源**（产品决策，最紧急：一次误点就毁掉 fork）
2. **P1-2 OSD 挂起恢复**（崩溃后遗症，用户难自查）
3. **P1-3 保活降频 + powercfg 超时**（长期开销与卡死风险）
4. **P2-1 / P2-2 / P2-3**（三个明确的 UI 状态错误，改动都很小）
5. **P2-4 / P2-5 / P2-6**（首屏闪烁、重复请求、迁移矛盾）
6. **P2-7 + P3-4 / P3-7 / P3-8**（视觉与一致性收尾）
7. **P3-5**（发布形态，需确认是否要 self-contained）

---

## 修复记录（2026-10-28 同日）

> 验证：`dotnet build -t:Rebuild` **0 错误 0 警告**（原 23 条警告清零）；`npm run type-check` 0 错误；改动文件 `eslint` 0 告警。
> 版本：`AppVersion` 10.19.74 → **10.19.75**（按 README「第三位=修复」规则）；README 徽章与三处版本引用同步。

### 已修

| 编号 | 处理方式 |
| --- | --- |
| P1-1 | 更新源改为 `Counkery/JiaolongControl`；只匹配 `*-win-x64.zip`；**不再下载后自动执行安装包**，改为下载 zip → 资源管理器定位 + 提示手动解压覆盖；按钮文案由「立即更新」改为「下载更新包」；临时文件名按 URL 取，保证 `.zip` 不会被改名成 `.exe` |
| P1-2 | 原厂软件已卸载，保留能力但做成无害：`Start()` 先 `ResumeStaleUtilities()` 清掉历史挂起（含挂起计数叠加），`Stop()` 先等轮询线程退出再恢复；进程不存在时轮询间隔 80ms → **1000ms**，找到进程才回到 200ms |
| P1-3 | `RunPowerCfg`/`RunPowerCfgWithOutput` 改为**先异步读输出再 `WaitForExit(timeout)`**，超时真正生效且不再死锁；`ReapplyProcessorPolicy` 增加**签名 + 5 分钟兜底 + 睡眠唤醒 force** 节流，日常从「每 30s 11 个 powercfg」降为「配置变化时/每 5 分钟一次」；EC 功耗墙仍每 30s 保活 |
| P2-1 | Logo 灯改用 `:model-value` + `@change`，按 `res.Success` 判断；失败不回写状态（开关自动停在原位）并弹错误；`Get().Success` 作为初始状态保留（后端约定就是「Success 即灯态」） |
| P2-2 | 风扇曲线 `onUnmounted` 时若有未触发的防抖定时器，先 `autoSave()` 再清理，切页不再丢最后一次改动 |
| P2-3 | 键盘渐变：`gradientRunning=false` 移入成功分支，失败不再提前显示已停止 |
| P2-4 | 设置页条件卡片：配置未加载完成时不再显示依赖项 |
| P2-5 | 配置拉取：直接复用进行中的 `fetchPromise`，去掉「await 后再判断」的失效逻辑 |
| P2-6 | `ConfigSerializer.Initialize` 重建后 `return`，不再被旧对象 `Update` 覆盖 |
| P2-7 | `CpuDie.vue` 去掉固定 200×200，改 `w-full h-full`，适配 64×64 容器 |
| P3-1 | `Stop()` 超时未退出时不再 Dispose CTS（避免 `ObjectDisposedException`），只记 Warn |
| P3-2/3 | 遥测轮询加重入保护；静态 GPU 信息用模块级 Promise 去重 |
| P3-4 | 删除 C# 不存在的 `TdcLimitMp1/Rsmu`、`SetCPUMaxState/GetCPUMaxState` 声明；`Fan.SetFanSpeed` 改四舍五入+夹取，非整百不再抛异常 |
| P3-6 | 移除 `BatteryThresholdLauncher` 里写死的 `D:\Application\...` |
| P3-7 | README 版本与 csproj 对齐（10.19.75） |
| P3-8 | 23 条警告清零：`Advapi32`/`ChangeServiceConfig` 可选参数改可空、`Data` 改 `object?`、`_saveTimer` 改 `Timer?`、`new` 修饰隐藏成员、去掉未用 catch 变量、`DriverDate`/`Speed` 空值处理 |
| 其他 | `.gitignore` 里 GBK 乱码注释改为 UTF-8 |

### 仍待决策（1 项）

| 编号 | 事项 | 说明 |
| --- | --- | --- |
| P3-5 | 便携包是否改 self-contained | 当前 `release.yml` 的 `dotnet publish` 没有 `-r win-x64 --self-contained`，解压后仍需 .NET 8 Desktop Runtime。改自包含会让 zip 从约 70MB 增到约 150MB；未擅自改动，建议在 README 里先写明依赖，或后续加发布参数 |

---

*第 1–5 节为审查快照；本节的「已修」为当日实际改动，冲突处以本节为准。*
