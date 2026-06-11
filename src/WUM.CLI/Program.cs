// src/WUM.CLI/Program.cs
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
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
            // ── Admin check ───────────────────────────────────────────────
#if !DEBUG
            WUM.CLI.Helpers.AdminHelper.RequireAdmin();
#else
            if (!WUM.CLI.Helpers.AdminHelper.IsRunningAsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "\n  [DEBUG] Running without admin " +
                    "- some features will not work.\n");
                Console.ResetColor();
            }
#endif

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
    }
}