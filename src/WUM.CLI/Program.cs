// src/WUM.CLI/Program.cs
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WUM.CLI.Commands;
using WUM.CLI.Helpers;
using WUM.Core.Helpers;
using WUM.Core.Services;

namespace WUM.CLI
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            // ── Console encoding ──────────────────────────────────────────
            // Force UTF-8 so glyphs (✓ ✗ ↳ ⟳ ● ○) render instead of '?'.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch { /* redirected stream — ignore */ }

            // ── Developer info short-circuit ──────────────────────────────
            if (args.Any(a => a == "--info"))
            {
                PrintDeveloperInfo();
                return 0;
            }

            // Admin is NOT required to launch. Read-only commands (status, list,
            // search, history, --version, --help) run as standard user. Commands
            // that modify the system enforce admin via AdminHelper.RequireAdmin().

            // ── Logger setup ──────────────────────────────────────────────
            string logDir = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "WUM", "logs");

            try { Directory.CreateDirectory(logDir); }
            catch { /* ignore if cannot create */ }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(logDir, "wum-.log"),
                    rollingInterval:        RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss} " +
                        "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("WUM started. Args: {Args}", string.Join(" ", args));

            // ── DI container ──────────────────────────────────────────────
            ServiceProvider services;
            try
            {
                services = new ServiceCollection()
                    .AddSingleton<RegistryHelper>()
                    .AddSingleton<IUpdateService,    UpdateService>()
                    .AddSingleton<IPauseService,     PauseService>()
                    .AddSingleton<IHistoryService,   HistoryService>()
                    .AddSingleton<ISchedulerService, SchedulerService>()
                    .AddSingleton<ISettingsService,  SettingsService>()
                    .BuildServiceProvider();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n  Failed to initialize services: " +
                                  ex.Message);
                Console.ResetColor();
                return 1;
            }

            // ── Root command ──────────────────────────────────────────────
            var root = new RootCommand(
                "wum - Windows Update Manager CLI\n" +
                "Manage Windows Updates from the command line.\n" +
                "Requires Administrator privileges.");

            // Global flag, handled in Main before the pipeline runs.
            root.AddGlobalOption(new Option<bool>("--info", "Show developer / build information"));

            // ── Register all commands ─────────────────────────────────────
            root.AddCommand(new StatusCommand(services).Build());
            root.AddCommand(new ListCommand(services).Build());
            root.AddCommand(new SearchCommand(services).Build());
            root.AddCommand(new InstallCommand(services).Build());
            root.AddCommand(new UninstallCommand(services).Build());
            root.AddCommand(new HideCommand(services).Build());
            root.AddCommand(new HistoryCommand(services).Build());
            root.AddCommand(new PauseCommand(services).Build());
            root.AddCommand(new ScheduleCommand(services).Build());
            root.AddCommand(new SettingsCommand(services).Build());
            root.AddCommand(new RebootCommand(services).Build());
            root.AddCommand(new DiagnoseCommand(services).Build());

            // ── Build pipeline ────────────────────────────────────────────
            var parser = new CommandLineBuilder(root)
                .UseDefaults()
                .UseExceptionHandler((ex, ctx) =>
                {
                    Log.Error(ex, "Unhandled exception");
                    Console.WriteLine();
                    ConsoleRenderer.Error("Error: " + ex.Message);
                    Console.WriteLine();
                    ctx.ExitCode = 1;
                })
                .Build();

            // ── Invoke ────────────────────────────────────────────────────
            int exitCode = await parser.InvokeAsync(args);

            Log.Information("WUM exited with code {Code}", exitCode);
            await Log.CloseAndFlushAsync();

            return exitCode;
        }

        // ── Developer / build information ─────────────────────────────────
        private static void PrintDeveloperInfo()
        {
            var asm = Assembly.GetEntryAssembly();

            string version =
                asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm?.GetName().Version?.ToString()
                ?? "unknown";

            int plus = version.IndexOf('+');
            if (plus >= 0) version = version.Substring(0, plus);

            // Short commit hash from informational version (e.g. "+abc1234").
            string commit = plus >= 0
                ? asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion?.Substring(plus + 1) ?? "—"
                : "—";
            if (commit.Length > 12) commit = commit.Substring(0, 12);

            // Build date from assembly file timestamp.
            string buildDate = "—";
            try
            {
                string? loc = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                    buildDate = File.GetLastWriteTime(loc)
                        .ToString("yyyy-MM-dd HH:mm");
            }
            catch { /* unavailable — ignore */ }

            Console.WriteLine();
            ConsoleRenderer.Header("  WUM - Windows Update Manager CLI");

            ConsoleRenderer.SectionHeader("Application");
            WriteInfoLine("Version",    version,                          ConsoleColor.Green,  "●");
            WriteInfoLine("Commit",     commit,                           ConsoleColor.Yellow, "❯");
            WriteInfoLine("Build Date", buildDate,                        ConsoleColor.White,  "⏱");
            WriteInfoLine("Author",     "Subroto Saha",                   ConsoleColor.White,  "✎");
            WriteInfoLine("License",    "MIT",                            ConsoleColor.White,  "§");
            WriteInfoLine("Repository", "https://github.com/isubroto/wum",ConsoleColor.Cyan,   "⎇");

            ConsoleRenderer.SectionHeader("Runtime");
            WriteInfoLine("Framework",  RuntimeInformation.FrameworkDescription, ConsoleColor.White, "⚙");
            WriteInfoLine("CLR",        Environment.Version.ToString(),          ConsoleColor.White, "⚙");
            WriteInfoLine("Process",    (Environment.Is64BitProcess ? "64-bit" : "32-bit")
                                        + " (" + RuntimeInformation.ProcessArchitecture + ")",
                                        ConsoleColor.White, "▣");

            ConsoleRenderer.SectionHeader("System");
            WriteInfoLine("OS",         RuntimeInformation.OSDescription,          ConsoleColor.White, "🖥");
            WriteInfoLine("OS Arch",    RuntimeInformation.OSArchitecture.ToString()
                                        + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"),
                                        ConsoleColor.White, "▣");
            WriteInfoLine("Machine",    Environment.MachineName,                   ConsoleColor.White, "⌂");
            WriteInfoLine("User",       Environment.UserName,                      ConsoleColor.White, "☺");
            WriteInfoLine("CPU Cores",  Environment.ProcessorCount.ToString(),     ConsoleColor.White, "⊞");
            Console.WriteLine();
        }

        private static void WriteInfoLine(
            string label, string value, ConsoleColor valueColor, string icon = "•")
        {
            ConsoleRenderer.Inline("  " + icon + " ", ConsoleColor.DarkCyan);
            ConsoleRenderer.Inline(label.PadRight(12), ConsoleColor.DarkGray);
            Console.ForegroundColor = valueColor;
            Console.WriteLine(value);
            Console.ResetColor();
        }
    }
}