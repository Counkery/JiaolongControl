using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using log4net;

namespace JiaoLongControl.Server.Core.Utils
{
    public class InnoUpdater
    {
        private readonly ILog Logger= LogManager.GetLogger(typeof(InnoUpdater));
        private readonly string _currentVersion;

        /// <summary>本 fork 的发布仓库（便携 zip 由 .github/workflows/release.yml 生成）。</summary>
        private const string RepoApiUrl = "https://api.github.com/repos/Counkery/JiaolongControl/releases/latest";

        /// <summary>本 fork 便携包的命名后缀，避免误匹配其它形态的产物。</summary>
        private const string PortableAssetSuffix = "-win-x64.zip";

        public InnoUpdater(string currentVersion)
        {
            _currentVersion = currentVersion;
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                string jsonResponse = await client.GetStringAsync(RepoApiUrl);
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;
                string latestVersion = root.GetProperty("tag_name").GetString() ?? "";
                if (string.IsNullOrEmpty(latestVersion)) return;
                if (!IsNewerVersion(latestVersion, _currentVersion))
                {
                    return;
                }

                string skippedVersion = UpdateSettings.GetSkippedVersion();
                if (latestVersion == skippedVersion)
                {
                    return; 
                }
                string releaseNotes = root.GetProperty("body").GetString() ?? "";
                string downloadUrl = "";
                var assets = root.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    string assetName = asset.GetProperty("name").GetString() ?? "";
                    if (!assetName.EndsWith(PortableAssetSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    break;
                }
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Logger.Info($"最新 release {latestVersion} 未附带 {PortableAssetSuffix}，跳过更新提示");
                    return;
                }
                bool shouldUpdate = false;
                bool shouldSkip = false;
                string downloadPath = "";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new UpdateDialog(latestVersion, _currentVersion, releaseNotes, downloadUrl);
                    if (dialog.ShowDialog() == true)
                    {
                        if (dialog.Result == UpdateDialog.UpdateChoice.UpdateNow)
                        {
                            shouldUpdate = true;
                            downloadPath = dialog.DownloadedFilePath;
                        }
                        else if (dialog.Result == UpdateDialog.UpdateChoice.SkipVersion)
                        {
                            shouldSkip = true;
                        }
                    }
                });

                if (shouldSkip)
                {
                    UpdateSettings.SaveSkippedVersion(latestVersion);
                    return;
                }

                if (shouldUpdate)
                {
                    // 本 fork 发布的是便携 zip，无法静默安装：定位到文件，由用户解压覆盖
                    if (downloadPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        RevealPortablePackage(downloadPath);
                    else
                        ExecuteInstaller(downloadPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"更新检查失败: {ex.Message}");
            }
        }

        /// <summary>便携包已下载：打开资源管理器定位到 zip，并提示手动解压覆盖。</summary>
        private void RevealPortablePackage(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });

                MessageBox.Show(
                    $"更新包已下载到:\n{filePath}\n\n" +
                    "请先退出本程序，再把 zip 内的文件解压覆盖到原目录（config 目录可保留）。",
                    "便携包已下载",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开下载位置失败: {ex.Message}\n\n文件位置: {filePath}",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExecuteInstaller(string filePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"安装程序执行失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var vLatest = new AppVersion(latest);
                var vCurrent = new AppVersion(current);

                return vLatest.CompareTo(vCurrent) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 自定义版本解析类
        /// </summary>
        private class AppVersion : IComparable<AppVersion>
        {
            public Version BaseVersion { get; }
            public int SuffixPriority { get; } // 1: Alpha(.A), 2: Beta(.B), 3: Stable

            public AppVersion(string versionStr)
            {
                versionStr = versionStr.Trim();
                if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    versionStr = versionStr[1..];

                string suffix = "";
                int lastDot = versionStr.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string lastPart = versionStr[(lastDot + 1)..];
                    if (lastPart is "A" or "B")
                    {
                        suffix = lastPart;
                        versionStr = versionStr[..lastDot];
                    }
                }

                BaseVersion = Version.TryParse(versionStr, out Version? parsedBase)
                    ? parsedBase
                    : new Version(0, 0, 0);
                SuffixPriority = suffix switch
                {
                    "A" => 1,
                    "B" => 2,
                    _ => 3
                };
            }
            public int CompareTo(AppVersion? other)
            {
                if (other == null) return 1;
                int baseComparison = BaseVersion.CompareTo(other.BaseVersion);
                if (baseComparison != 0)
                {
                    return baseComparison;
                }
                return SuffixPriority.CompareTo(other.SuffixPriority);
            }
        }
    }
}