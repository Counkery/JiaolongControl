# Phase 0 基线验收清单（7945HX + RTX 4060）

工程路径：`D:\Application\JiaolongControl`（分支 `remake/jiaolong-control`，版本 10.19.33）。

## 构建

```bash
# SDK: .NET 8
cd JiaoLongControl/Client
npm ci
npm run build

cd ..
dotnet build JiaoLongControl.csproj -c Release -p:Platform=x64
```

## 真机验收（管理员运行）

| # | 项 | 通过标准 | 结果 |
|---|---|---|---|
| 1 | 启动 / 托盘 | 单实例、托盘图标、无闪退 | |
| 2 | PawnIO 驱动模式 | 设置页可选；无 WinRing0 依赖 | |
| 3 | 性能模式切换 | Quiet/Balance/Performance 生效，OSD 不双弹 | |
| 4 | 手动风扇 | 转速变化可见 | |
| 5 | 自动风扇 / 曲线 | 温度变化时爬升下降平滑 | |
| 6 | SPL/SPPT/温度墙 | CPU 页写入后 HWInfo/Ryzen 侧可见或稳定 | |
| 7 | 功耗保活 | 开启后约 30s 周期；日志无海量刷屏 | |
| 8 | RGB 开关 | 关灯即时；渐变停止不回写旧色 | |
| 9 | FN / 触控板热键 | 一次按键一次 OSD | |
| 10 | 开机自启 | `--boot` 最小化策略可应用 | |
| 11 | 独显直连 | 提示风险；切换可完成（可二期打磨） | |
| 12 | 勿双开闭源控制中心 | 同时只跑一套 | |

## 已知风险

- 硬件写入可能导致不稳定：保留默认功耗档，先小步验证。
- 需管理员权限加载 Blding/PawnIO。
