// src/WUM.CLI/Commands/UpdateCommand.cs
using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using WUM.CLI.Helpers;

namespace WUM.CLI.Commands
{
    /// <summary>
    /// Self-update. Checks the latest GitHub release, compares versions, and
    /// (unless --check) downloads the release MSI and runs msiexec to upgrade.
    /// </summary>
    public class UpdateCommand
    {
        private const string Owner = "isubroto";
        private const string Repo  = "wum";
        private const string LatestReleaseApi =
            "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        // GitHub requires a User-Agent on API requests.
        private static readonly HttpClient Http = CreateClient();

        public UpdateCommand(IServiceProvider sp)
        {
            // No services needed — self-update talks to GitHub + msiexec.
        }

        public Command Build()
        {
            var cmd = new Command(
                "update",
                "Check for a newer wum release and self-update");
            cmd.AddAlias("upgrade");

            var checkOpt = new Option<bool>(
                new[] { "--check", "-c" },
                "Only check for a newer version; do not install");
            var forceOpt = new Option<bool>(
                new[] { "--force", "-f", "--yes", "-y" },
                "Skip the confirmation prompt and install");

            cmd.AddOption(checkOpt);
            cmd.AddOption(forceOpt);

            cmd.SetHandler(async (ctx) =>
            {
                bool check = ctx.ParseResult.GetValueForOption(checkOpt);
                bool force = ctx.ParseResult.GetValueForOption(forceOpt);
                ctx.ExitCode = await RunAsync(check, force);
            });

            return cmd;
        }

        // Exit codes: 0 = up to date / installed, 1 = error, 2 = update available
        // (only in --check mode, so scripts/schedulers can detect it).
        private async Task<int> RunAsync(bool checkOnly, bool force)
        {
            Console.WriteLine();
            ConsoleRenderer.Header("  WUM — Self Update");
            Console.WriteLine();

            Version current = GetCurrentVersion();
            ConsoleRenderer.Field("Current", "v" + current, ConsoleColor.White);

            ReleaseInfo? release = null;
            await ConsoleRenderer.ShowSpinnerAsync(
                "Checking GitHub for the latest release...", async () =>
                {
                    release = await FetchLatestReleaseAsync();
                }, timeoutSeconds: 30);

            if (release is null)
            {
                Console.WriteLine();
                ConsoleRenderer.Error(
                    "Could not reach GitHub or parse the latest release.");
                ConsoleRenderer.Hint(
                    "Check your connection or download manually: " +
                    "https://github.com/" + Owner + "/" + Repo + "/releases/latest");
                Console.WriteLine();
                return 1;
            }

            ConsoleRenderer.Field("Latest",  "v" + release.Version, ConsoleColor.Cyan);
            Console.WriteLine();

            // ── Up to date ────────────────────────────────────────────────
            if (release.Version <= current)
            {
                ConsoleRenderer.Success("✓ You are on the latest version.");
                Console.WriteLine();
                return 0;
            }

            ConsoleRenderer.Warning(
                "● Update available: v" + current + " -> v" + release.Version);
            Console.WriteLine();

            // ── Check-only mode ──────────────────────────────────────────
            if (checkOnly)
            {
                ConsoleRenderer.Hint("Run 'wum update' to install it.");
                Console.WriteLine();
                return 2;
            }

            if (string.IsNullOrEmpty(release.MsiUrl))
            {
                ConsoleRenderer.Error(
                    "Latest release has no MSI asset — cannot auto-install.");
                ConsoleRenderer.Hint(
                    "Download manually: " + release.HtmlUrl);
                Console.WriteLine();
                return 1;
            }

            // Installing the MSI writes to Program Files and requires elevation.
            AdminHelper.RequireAdmin();

            if (!force && !ConsoleRenderer.Confirm(
                    "  Download and install v" + release.Version + " now?"))
            {
                ConsoleRenderer.Muted("  Cancelled.");
                Console.WriteLine();
                return 0;
            }

            // ── Download MSI ─────────────────────────────────────────────
            string msiPath = Path.Combine(
                Path.GetTempPath(), "wum-" + release.Version + ".msi");

            bool downloaded = false;
            await ConsoleRenderer.ShowSpinnerAsync(
                "Downloading " + release.MsiName + "...", async () =>
                {
                    downloaded = await DownloadFileAsync(release.MsiUrl!, msiPath);
                }, timeoutSeconds: 300);

            if (!downloaded || !File.Exists(msiPath))
            {
                Console.WriteLine();
                ConsoleRenderer.Error("Download failed.");
                ConsoleRenderer.Hint("Download manually: " + release.HtmlUrl);
                Console.WriteLine();
                return 1;
            }

            ConsoleRenderer.Success("✓ Downloaded to " + msiPath);
            Console.WriteLine();

            // ── Run installer ────────────────────────────────────────────
            ConsoleRenderer.Info("  Launching installer (msiexec)...");
            ConsoleRenderer.Muted(
                "  wum will close while the upgrade completes.");
            Console.WriteLine();

            int rc = RunMsi(msiPath);

            if (rc == 0 || rc == 3010)
            {
                ConsoleRenderer.Success(
                    "✓ Update to v" + release.Version + " installed.");
                if (rc == 3010)
                    ConsoleRenderer.Hint("A reboot is required to finish.");
                ConsoleRenderer.Hint("Run 'wum --info' to confirm the new version.");
                Console.WriteLine();
                return 0;
            }

            ConsoleRenderer.Error("Installer exited with code " + rc + ".");
            ConsoleRenderer.Hint("Install manually: " + msiPath);
            Console.WriteLine();
            return 1;
        }

        // ── GitHub release fetch ─────────────────────────────────────────
        private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
        {
            try
            {
                using var resp = await Http.GetAsync(LatestReleaseApi);
                if (!resp.IsSuccessStatusCode) return null;

                string body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var t)
                    ? t.GetString() ?? "" : "";
                Version? ver = NormalizeVersion(tag);
                if (ver is null) return null;

                string htmlUrl = root.TryGetProperty("html_url", out var h)
                    ? h.GetString() ?? "" : "";

                string? msiUrl = null, msiName = null;
                if (root.TryGetProperty("assets", out var assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n)
                            ? n.GetString() ?? "" : "";
                        if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        {
                            msiName = name;
                            msiUrl  = asset.TryGetProperty(
                                "browser_download_url", out var u)
                                ? u.GetString() : null;
                            break;
                        }
                    }
                }

                return new ReleaseInfo(ver, msiUrl, msiName ?? "installer", htmlUrl);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> DownloadFileAsync(string url, string dest)
        {
            try
            {
                using var resp = await Http.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return false;

                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var fs  = File.Create(dest);
                await src.CopyToAsync(fs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── msiexec ──────────────────────────────────────────────────────
        // /qb = basic UI (shows progress, no prompts). /norestart so we can
        // report the 3010 reboot-required code instead of an abrupt restart.
        private static int RunMsi(string msiPath)
        {
            try
            {
                var psi = new ProcessStartInfo("msiexec.exe",
                    "/i \"" + msiPath + "\" /qb /norestart")
                {
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                if (p is null) return -1;
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                ConsoleRenderer.Error("Failed to start msiexec: " + ex.Message);
                return -1;
            }
        }

        // ── Version helpers ──────────────────────────────────────────────
        private static Version GetCurrentVersion()
        {
            var asm = Assembly.GetEntryAssembly();
            string raw =
                asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                ?? asm?.GetName().Version?.ToString()
                ?? "0.0.0.0";

            int plus = raw.IndexOf('+');           // strip "+<commit>" if present
            if (plus >= 0) raw = raw.Substring(0, plus);

            return NormalizeVersion(raw) ?? new Version(0, 0, 0, 0);
        }

        // Accepts "v0.3.0", "0.3.0.58", etc. Pads to a 4-part Version so that
        // 3-part tags compare correctly against 4-part assembly versions
        // (System.Version treats a missing component as -1, not 0).
        private static Version? NormalizeVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().TrimStart('v', 'V');

            int plus = s.IndexOf('+'); if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-'); if (dash >= 0) s = s.Substring(0, dash);

            var parts = s.Split('.');
            int[] n = { 0, 0, 0, 0 };
            for (int i = 0; i < 4 && i < parts.Length; i++)
                if (!int.TryParse(parts[i], out n[i])) return null;

            return new Version(n[0], n[1], n[2], n[3]);
        }

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("wum-self-update");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        private sealed record ReleaseInfo(
            Version Version, string? MsiUrl, string MsiName, string HtmlUrl);
    }
}
