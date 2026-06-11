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

            Console.WriteLine();
            ConsoleRenderer.Header("  WUM - Windows Update Manager CLI");
            Console.WriteLine();
            WriteInfoLine("Version",   version);
            WriteInfoLine("Author",    "Subroto Saha");
            WriteInfoLine("License",   "MIT");
            WriteInfoLine("Repository","https://github.com/isubroto/wum");
            WriteInfoLine("Runtime",   RuntimeInformation.FrameworkDescription);
            WriteInfoLine("OS",        RuntimeInformation.OSDescription);
            WriteInfoLine("Arch",      RuntimeInformation.OSArchitecture.ToString());
            Console.WriteLine();
        }

        private static void WriteInfoLine(string label, string value)
        {
            ConsoleRenderer.Inline("  " + label.PadRight(12), ConsoleColor.DarkGray);
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(value);
            Console.ResetColor();
        }
    }
}