// src/WUM.CLI/Interactive/InteractiveShell.cs
using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WUM.CLI.Interactive
{
    public sealed class InteractiveShell
    {
        internal enum SuggestionKind { Command, Subcommand, Option, Value, Session }

        internal readonly record struct CommandSuggestion(
            string Command, string Usage, string Description,
            string InsertText, int ReplaceStart, int ReplaceLength, SuggestionKind Kind)
        {
            public CommandSuggestion(string command, string usage, string description)
                : this(command, usage, description, command, 0, command.Length, SuggestionKind.Command) { }
        }

        private sealed record OptionSpec(string[] Names, string Usage, string Description, bool TakesValue = false);
        private sealed record SubcommandSpec(string Name, string Usage, string Description, OptionSpec[] Options);
        private sealed record CommandSpec(string Name, string Usage, string Description, OptionSpec[] Options, SubcommandSpec[] Subcommands);

        private static OptionSpec Opt(string name, string usage, string description, bool takesValue = false) => new(new[] { name }, usage, description, takesValue);
        private static OptionSpec Opt(string[] names, string usage, string description, bool takesValue = false) => new(names, usage, description, takesValue);

        private static readonly CommandSuggestion[] CommandCatalog = new[]
        {
            new CommandSuggestion("/status",   "/status [options]",              "See what Windows Update is doing right now"),
            new CommandSuggestion("/list",     "/list [filters]",                "Browse available, installed, or hidden updates"),
            new CommandSuggestion("/search",   "/search <query>",                "Find updates by KB, title, or category"),
            new CommandSuggestion("/install",  "/install [KB...] [filters]",     "Install one, many, or all matching updates"),
            new CommandSuggestion("/uninstall","/uninstall <KB>",                "Remove an installed update"),
            new CommandSuggestion("/hide",     "/hide add|remove|list",          "Hide updates you don't want to see"),
            new CommandSuggestion("/history",  "/history [options]",             "Review past installs and failures"),
            new CommandSuggestion("/pause",    "/pause [--days N|resume]",       "Pause or resume Windows Update"),
            new CommandSuggestion("/schedule", "/schedule show|set|clear",       "Plan a weekly install window"),
            new CommandSuggestion("/settings", "/settings show|set|reset",       "View or change WUM preferences"),
            new CommandSuggestion("/reboot",   "/reboot [options]",              "Schedule, force, or cancel a reboot"),
            new CommandSuggestion("/diagnose", "/diagnose [options]",            "Run Windows Update diagnostics"),
            new CommandSuggestion("/commands", "/commands", "Browse all commands grouped by task"),
            new CommandSuggestion("/help",     "/help [command]", "Show help — add a command for specifics"),
            new CommandSuggestion("/keys",     "/keys", "See keyboard shortcuts"),
            new CommandSuggestion("/clear",    "/clear", "Clear the screen"),
            new CommandSuggestion("/version",  "/version", "Show WUM version"),
            new CommandSuggestion("/info",     "/info", "Show build & developer info"),
            new CommandSuggestion("/exit",     "/exit", "Leave interactive mode"),
            new CommandSuggestion("/quit",     "/quit", "Alias for /exit"),
            new CommandSuggestion("/?",        "/?", "Alias for /help")
        };

        private sealed record CommandGroup(string Label, string Glyph, string[] Commands);
        private static readonly CommandGroup[] CommandGroups = new[]
        {
            new CommandGroup("Look around",  "👀", new[] { "/status", "/list", "/search", "/history" }),
            new CommandGroup("Take action",  "⚡", new[] { "/install", "/uninstall", "/hide" }),
            new CommandGroup("Stay in control","🛡️", new[] { "/pause", "/schedule", "/settings", "/reboot", "/diagnose" }),
            new CommandGroup("This session", "💬", new[] { "/commands", "/help", "/keys", "/clear", "/version", "/info", "/exit" })
        };

        private static readonly HashSet<string> KnownCommandSet = new(CommandCatalog.Select(c => c.Command), StringComparer.OrdinalIgnoreCase);

        private static readonly CommandSpec[] CommandSpecs = new[]
        {
            new CommandSpec("status", "/status [--json] [--verbose] [--refresh]",
                "See service, reboot, pause, and pending-update status at a glance.",
                new[] { Opt("--json", "--json", "Output as JSON"), Opt(new[] { "--verbose", "-v" }, "--verbose, -v", "Show extra detail"), Opt("--refresh", "--refresh", "Force a fresh update scan first") },
                Array.Empty<SubcommandSpec>()),
            new CommandSpec("list", "/list [filters] [--json]",
                "Browse updates. Add filters to narrow down what you see.",
                new[] { Opt("--security", "--security", "Only security updates"), Opt("--critical", "--critical", "Only critical updates"), Opt("--all", "--all", "Install every available update"), Opt("--dry-run", "--dry-run", "Preview without changing anything") },
                Array.Empty<SubcommandSpec>()),
            new CommandSpec("install", "/install [KB...] [filters] [--dry-run]",
                "Install updates. Pass KBs, use filters, or pick --all.",
                new[] { Opt("--security", "--security", "Only security updates"), Opt("--all", "--all", "Install every available update"), Opt("--dry-run", "--dry-run", "Preview without changing anything") },
                Array.Empty<SubcommandSpec>()),
            new CommandSpec("hide", "/hide add|remove|list", "Hide updates you don't want, or bring them back.", Array.Empty<OptionSpec>(),
                new[] { new SubcommandSpec("add", "/hide add <id>", "Hide an update", Array.Empty<OptionSpec>()), new SubcommandSpec("remove", "/hide remove <id>", "Unhide an update", Array.Empty<OptionSpec>()), new SubcommandSpec("list", "/hide list", "List hidden updates", Array.Empty<OptionSpec>()) }),
            new CommandSpec("schedule", "/schedule show|set|clear", "Plan a recurring weekly install window.", Array.Empty<OptionSpec>(),
                new[] { new SubcommandSpec("show", "/schedule show", "See the current schedule", Array.Empty<OptionSpec>()), new SubcommandSpec("set", "/schedule set --day Sunday --time 02:00", "Configure the schedule", new[] { Opt("--day", "--day <day>", "Day of week", true), Opt("--time", "--time <HH:mm>", "Time (24-hour)", true) }), new SubcommandSpec("clear", "/schedule clear", "Remove the schedule", Array.Empty<OptionSpec>()) })
        };

        private static readonly Dictionary<string, CommandSpec> CommandSpecByName = CommandSpecs.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        private const string CommandPrompt = "wum › ";
        private const int MaxHistoryEntries = 200;
        private const int MaxVisibleSuggestions = 10;

        private readonly Func<string[], Task<int>> _invokeAsync;
        private readonly TextReader _input;
        private readonly TextWriter _output;
        private readonly Action _clearTerminal;
        private readonly Action _printDeveloperInfo;
        private readonly bool _useAnsi;
        private readonly bool _useKeyEditor;
        private readonly List<string> _history = new();
        
        private int _lastRenderedHeight;
        private bool _firstInterrupt = true;

        public InteractiveShell(Parser parser, Action printDeveloperInfo)
            : this(args => parser.InvokeAsync(args), Console.In, Console.Out, ClearConsoleSafely, printDeveloperInfo, !Console.IsOutputRedirected, CanUseKeyEditor()) { }

        public InteractiveShell(Func<string[], Task<int>> invokeAsync, TextReader input, TextWriter output, Action clearTerminal, Action printDeveloperInfo, bool useAnsi = false, bool useKeyEditor = false)
        {
            _invokeAsync = invokeAsync ?? throw new ArgumentNullException(nameof(invokeAsync));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _clearTerminal = clearTerminal ?? throw new ArgumentNullException(nameof(clearTerminal));
            _printDeveloperInfo = printDeveloperInfo ?? throw new ArgumentNullException(nameof(printDeveloperInfo));
            _useAnsi = useAnsi;
            _useKeyEditor = useKeyEditor;
            if (_useKeyEditor) LoadHistory();
        }

        public async Task<int> RunAsync()
        {
            Console.OutputEncoding = Encoding.UTF8;
            ConsoleCancelEventHandler? cancelHandler = null;
            
            if (_useKeyEditor)
            {
                cancelHandler = (s, e) =>
                {
                    if (_firstInterrupt)
                    {
                        e.Cancel = true;
                        _firstInterrupt = false;
                    }
                    else
                    {
                        WriteStyledLine("Goodbye! See you next time.", Ansi.Green + Ansi.Bold);
                        e.Cancel = false;
                    }
                };
                Console.CancelKeyPress += cancelHandler;
            }

            try
            {
                int exitCode = 0;

                _clearTerminal();
                PrintWelcome();

                while (true)
                {
                    string? line = _useKeyEditor ? ReadKeyEditor() : ReadLinePlain();
                    if (line == null) return exitCode;

                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && !IsExitCommand(trimmed))
                        AddHistoryEntry(trimmed);

                    var result = await HandleLineAsync(trimmed);
                    exitCode = result.ExitCode;
                    if (result.ShouldExit) return exitCode;
                    if (trimmed.Length > 0) _firstInterrupt = true;
                }
            }
            finally
            {
                if (cancelHandler is not null) Console.CancelKeyPress -= cancelHandler;
            }
        }

        private string? ReadLinePlain()
        {
            _output.WriteLine();
            WriteRuleWithLabel("ready");
            WriteStyled(CommandPrompt, Ansi.Accent + Ansi.Bold);
            _output.Flush();
            return _input.ReadLine();
        }

        private string? ReadKeyEditor()
        {
            int editorTop = BeginEditorFrame();

            var buffer = new StringBuilder();
            int cursor = 0;
            int selectedIndex = 0;
            int historyIndex = _history.Count;
            string draftBeforeHistory = string.Empty;
            _lastRenderedHeight = 0;

            bool restoreControlC = TrySetTreatControlCAsInput(true, out bool previousTreatControlC);

            try
            {
                RenderEditor(buffer, cursor, selectedIndex, ref editorTop);

                while (true)
                {
                    var suggestions = GetCommandSuggestions(buffer.ToString(), cursor);
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                    bool control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
                    bool shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);

                    if (control && key.Key == ConsoleKey.C) 
                    { 
                        if (_firstInterrupt)
                        {
                            _firstInterrupt = false;
                            buffer.Clear();
                            cursor = 0;
                            selectedIndex = 0;
                            ResetHistory(ref historyIndex, ref draftBeforeHistory);
                            RenderEditor(buffer, cursor, selectedIndex, ref editorTop);
                            Console.SetCursorPosition(0, editorTop + _lastRenderedHeight - 1);
                            Console.Write("\u001b[2K");
                            WriteStyled("Press Ctrl+C again to exit, or type /exit to leave normally.", Ansi.Dim);
                            Console.SetCursorPosition(Math.Min(CommandPrompt.Length, Console.WindowWidth - 1), editorTop);
                            continue;
                        }
                        else
                        {
                            CommitEditor(buffer.ToString(), editorTop);
                            WriteStyledLine("Goodbye! See you next time.", Ansi.Green + Ansi.Bold);
                            return null; 
                        }
                    }
                    if (control && key.Key == ConsoleKey.L)
                    {
                        _clearTerminal();
                        PrintWelcome();
                        editorTop = BeginEditorFrame();
                        _lastRenderedHeight = 0;
                        RenderEditor(buffer, cursor, selectedIndex, ref editorTop);
                        continue;
                    }
                    if (control && key.Key == ConsoleKey.D && buffer.Length == 0)
                    {
                        CommitEditor(buffer.ToString(), editorTop);
                        WriteStyledLine("Goodbye! See you next time.", Ansi.Green + Ansi.Bold);
                        return null;
                    }

                    if (key.Key == ConsoleKey.Enter)
                    {
                        if (buffer.Length == 0)
                        {
                            RenderEditor(buffer, cursor, selectedIndex, ref editorTop);
                            continue;
                        }

                        if (ShouldCompleteOnEnter(buffer.ToString(), suggestions))
                        {
                            CompleteSuggestion(buffer, ref cursor, suggestions, selectedIndex);
                            selectedIndex = 0;
                            RenderEditor(buffer, cursor, selectedIndex, ref editorTop);
                            continue;
                        }
                        CommitEditor(buffer.ToString(), editorTop);
                        return buffer.ToString();
                    }

                    if (control && key.Key == ConsoleKey.A) cursor = 0;
                    else if (control && key.Key == ConsoleKey.E) cursor = buffer.Length;
                    else if (control && key.Key == ConsoleKey.U) { if (cursor > 0) { buffer.Remove(0, cursor); cursor = 0; ResetHistory(ref historyIndex, ref draftBeforeHistory); } }
                    else if (control && key.Key == ConsoleKey.K) { if (cursor < buffer.Length) { buffer.Remove(cursor, buffer.Length - cursor); ResetHistory(ref historyIndex, ref draftBeforeHistory); } }
                    else if (control && key.Key == ConsoleKey.W) { DeleteWordBeforeCursor(buffer, ref cursor); ResetHistory(ref historyIndex, ref draftBeforeHistory); }
                    else if (control && key.Key == ConsoleKey.P) { NavigateHistory(buffer, ref cursor, -1, ref historyIndex, ref draftBeforeHistory); selectedIndex = 0; }
                    else if (control && key.Key == ConsoleKey.N) { NavigateHistory(buffer, ref cursor, 1, ref historyIndex, ref draftBeforeHistory); selectedIndex = 0; }
                    else if (key.Key == ConsoleKey.Backspace) { if (cursor > 0) { buffer.Remove(cursor - 1, 1); cursor--; selectedIndex = 0; ResetHistory(ref historyIndex, ref draftBeforeHistory); } }
                    else if (key.Key == ConsoleKey.Delete || (control && key.Key == ConsoleKey.D)) { if (cursor < buffer.Length) { buffer.Remove(cursor, 1); selectedIndex = 0; ResetHistory(ref historyIndex, ref draftBeforeHistory); } }
                    else if (key.Key == ConsoleKey.LeftArrow) { if (control) MoveWordLeft(buffer.ToString(), ref cursor); else if (cursor > 0) cursor--; }
                    else if (key.Key == ConsoleKey.RightArrow)
                    {
                        if (control) MoveWordRight(buffer.ToString(), ref cursor);
                        else if (cursor < buffer.Length) cursor++;
                        else if (cursor == buffer.Length && suggestions.Count > 0)
                        {
                            string ghost = GetGhostCompletion(buffer.ToString(), cursor, suggestions, selectedIndex);
                            if (!string.IsNullOrEmpty(ghost)) { buffer.Append(ghost); cursor += ghost.Length; selectedIndex = 0; ResetHistory(ref historyIndex, ref draftBeforeHistory); }
                        }
                    }
                    else if (key.Key == ConsoleKey.Home) cursor = 0;
                    else if (key.Key == ConsoleKey.End) cursor = buffer.Length;
                    else if (key.Key == ConsoleKey.Escape) { buffer.Clear(); cursor = 0; selectedIndex = 0; ResetHistory(ref historyIndex, ref draftBeforeHistory); }
                    else if (key.Key == ConsoleKey.Tab)
                    {
                        if (suggestions.Count > 0 && shift) selectedIndex = selectedIndex <= 0 ? suggestions.Count - 1 : selectedIndex - 1;
                        else CompleteSuggestion(buffer, ref cursor, suggestions, selectedIndex);
                        selectedIndex = ClampSelectionIndex(buffer.ToString(), cursor, selectedIndex);
                        ResetHistory(ref historyIndex, ref draftBeforeHistory);
                    }
                    else if (key.Key == ConsoleKey.UpArrow)
                    {
                        if (ShouldNavigateHistory(buffer.ToString(), historyIndex)) { NavigateHistory(buffer, ref cursor, -1, ref historyIndex, ref draftBeforeHistory); selectedIndex = 0; }
                        else if (suggestions.Count > 0) selectedIndex = Math.Max(0, selectedIndex - 1);
                    }
                    else if (key.Key == ConsoleKey.DownArrow)
                    {
                        if (historyIndex < _history.Count) { NavigateHistory(buffer, ref cursor, 1, ref historyIndex, ref draftBeforeHistory); selectedIndex = 0; }
                        else if (suggestions.Count > 0) selectedIndex = Math.Min(suggestions.Count - 1, selectedIndex + 1);
                    }
                    else if (!char.IsControl(key.KeyChar)) { buffer.Insert(cursor, key.KeyChar); cursor++; selectedIndex = 0; ResetHistory(ref historyIndex, ref draftBeforeHistory); }

                    selectedIndex = ClampSelectionIndex(buffer.ToString(), cursor, selectedIndex);
                    RenderEditor(buffer, cursor, selectedIndex, ref editorTop);
                }
            }
            finally
            {
                if (restoreControlC) TrySetTreatControlCAsInput(previousTreatControlC, out _);
            }
        }

        private void CommitEditor(string buffer, int editorTop)
        {
            Console.Write("\u001b[?25l");
            try
            {
                for (int i = _lastRenderedHeight - 1; i >= 0; i--)
                {
                    Console.SetCursorPosition(0, editorTop + i);
                    Console.Write("\u001b[2K");
                }
                Console.SetCursorPosition(0, editorTop);
                Console.Write($"{Ansi.Accent}{Ansi.Bold}{CommandPrompt}{Ansi.Reset}");
                Console.Write(buffer);
            }
            finally { Console.Write("\u001b[?25h"); }
        }

        private void RenderEditor(StringBuilder buffer, int cursor, int selectedIndex, ref int editorTop)
        {
            Console.Write("\u001b[?25l");
            try
            {
                var suggestions = GetCommandSuggestions(buffer.ToString(), cursor);
                if (suggestions.Count == 0) selectedIndex = 0;
                else selectedIndex = Math.Clamp(selectedIndex, 0, suggestions.Count - 1);

                string ghostText = GetGhostCompletion(buffer.ToString(), cursor, suggestions, selectedIndex);

                int rowsNeeded = GetEditorRowsForSuggestions(suggestions.Count);
                int shift = ReserveVisibleRows(editorTop, rowsNeeded);
                editorTop -= shift;
                if (editorTop < 0) editorTop = 0;

                Console.SetCursorPosition(0, editorTop);
                Console.Write("\u001b[2K");
                Console.Write($"{Ansi.Accent}{Ansi.Bold}{CommandPrompt}{Ansi.Reset}");
                Console.Write(buffer.ToString());
                if (!string.IsNullOrEmpty(ghostText)) Console.Write($"{Ansi.Ghost}{ghostText}{Ansi.Reset}");

                int boxTop = editorTop + 1;
                int currentHeight = 1;

                if (suggestions.Count > 0)
                {
                    int width = GetRuleWidth();
                    string title = GetSuggestionPanelTitle(suggestions);
                    string bottomHint = "Tab ↹ complete · Enter ↵ run · ↑↓ navigate";

                    Console.SetCursorPosition(0, boxTop);
                    Console.Write("\u001b[2K");
                    Console.Write($"{Ansi.Rule}╭─ {Ansi.Reset}{Ansi.Muted}{title}{Ansi.Reset}{Ansi.Rule} " + new string('─', Math.Max(1, width - title.Length - 5)) + "╮" + Ansi.Reset);

                    for (int i = 0; i < suggestions.Count; i++)
                    {
                        Console.SetCursorPosition(0, boxTop + 1 + i);
                        Console.Write("\u001b[2K");
                        var s = suggestions[i];
                        string prefix = i == selectedIndex ? "▸ " : "  ";
                        string color = i == selectedIndex ? Ansi.Accent + Ansi.Bold : Ansi.Text;
                        string descColor = i == selectedIndex ? Ansi.Text : Ansi.Muted;

                        string usage = s.Usage.Length > 30 ? s.Usage.Substring(0, 30) : s.Usage.PadRight(30);
                        string desc = s.Description.Length > width - 38 ? s.Description.Substring(0, Math.Max(0, width - 38)) : s.Description;

                        Console.Write($"{Ansi.Rule}│{Ansi.Reset} {color}{prefix}{usage}{Ansi.Reset} {descColor}{desc}{Ansi.Reset}");
                        int used = 2 + 2 + 30 + 1 + desc.Length;
                        Console.Write(new string(' ', Math.Max(0, width - used - 1)));
                        Console.Write($"{Ansi.Rule}│{Ansi.Reset}");
                    }

                    Console.SetCursorPosition(0, boxTop + 1 + suggestions.Count);
                    Console.Write("\u001b[2K");
                    Console.Write($"{Ansi.Rule}╰─ {Ansi.Reset}{Ansi.Muted}{bottomHint}{Ansi.Reset}{Ansi.Rule} " + new string('─', Math.Max(1, width - bottomHint.Length - 5)) + "╯" + Ansi.Reset);

                    currentHeight += suggestions.Count + 2;
                }

                int statusTop = editorTop + currentHeight;
                Console.SetCursorPosition(0, statusTop);
                Console.Write("\u001b[2K");
                Console.Write($"{Ansi.Accent}↑↓{Ansi.Reset} {Ansi.Muted}history{Ansi.Reset} {Ansi.Rule}·{Ansi.Reset} {Ansi.Accent}Tab{Ansi.Reset} {Ansi.Muted}complete{Ansi.Reset} {Ansi.Rule}·{Ansi.Reset} {Ansi.Accent}Ctrl+L{Ansi.Reset} {Ansi.Muted}clear{Ansi.Reset} {Ansi.Rule}·{Ansi.Reset} {Ansi.Accent}Ctrl+C{Ansi.Reset} {Ansi.Muted}cancel{Ansi.Reset} {Ansi.Rule}·{Ansi.Reset} {Ansi.Accent}Ctrl+D{Ansi.Reset} {Ansi.Muted}exit{Ansi.Reset}");

                Console.SetCursorPosition(0, statusTop + 1);
                Console.Write("\u001b[2K");
                Console.Write($"{Ansi.Muted}● {GetSessionStatus()}{Ansi.Reset}");

                currentHeight += 2;

                if (_lastRenderedHeight > currentHeight)
                {
                    for (int i = currentHeight; i < _lastRenderedHeight; i++)
                    {
                        Console.SetCursorPosition(0, editorTop + i);
                        Console.Write("\u001b[2K");
                    }
                }
                _lastRenderedHeight = currentHeight;

                int cursorLeft = Math.Min(CommandPrompt.Length + cursor, Console.WindowWidth - 1);
                Console.SetCursorPosition(cursorLeft, editorTop);
            }
            finally { Console.Write("\u001b[?25h"); }
        }

        private async Task<LineResult> HandleLineAsync(string line)
        {
            if (line.Length == 0) return LineResult.Continue(0);

            if (IsExitCommand(line))
            {
                _output.WriteLine();
                WriteStyledLine("Take care — updates will keep working in the background.", Ansi.Muted);
                return LineResult.Exit(0);
            }

            if (!line.StartsWith("/", StringComparison.Ordinal))
            {
                PrintResponseHeader("hint");
                PrintSlashHint();
                PrintResponseFooter(0);
                return LineResult.Continue(1);
            }

            string command = GetCommandWord(line);
            if (IsHelpCommand(command)) { PrintResponseHeader(line); PrintHelp(line); PrintResponseFooter(0); return LineResult.Continue(0); }
            if (command.Equals("/commands", StringComparison.OrdinalIgnoreCase)) { PrintResponseHeader(line); PrintCommandPalette(); PrintResponseFooter(0); return LineResult.Continue(0); }
            if (command.Equals("/keys", StringComparison.OrdinalIgnoreCase)) { PrintResponseHeader(line); PrintKeyHelp(); PrintResponseFooter(0); return LineResult.Continue(0); }
            if (command.Equals("/clear", StringComparison.OrdinalIgnoreCase)) { _clearTerminal(); PrintWelcome(); return LineResult.Continue(0); }
            if (command.Equals("/version", StringComparison.OrdinalIgnoreCase)) { PrintResponseHeader(line); int code = await _invokeAsync(new[] { "--version" }); PrintResponseFooter(code); return LineResult.Continue(code); }
            if (command.Equals("/info", StringComparison.OrdinalIgnoreCase)) { PrintResponseHeader(line); _printDeveloperInfo(); PrintResponseFooter(0); return LineResult.Continue(0); }

            if (!KnownCommandSet.Contains(command)) { PrintResponseHeader(line); PrintUnknownCommand(command); PrintResponseFooter(1); return LineResult.Continue(1); }

            string commandLine = line.Substring(1).TrimStart();
            string[] args;
            try { args = CommandLineTokenizer.Tokenize(commandLine); }
            catch (FormatException ex) { PrintResponseHeader(line); _output.WriteLine(ex.Message); PrintResponseFooter(1); return LineResult.Continue(1); }

            PrintResponseHeader(line);
            int exitCode = await _invokeAsync(args);
            PrintResponseFooter(exitCode);
            return LineResult.Continue(exitCode);
        }

        private void PrintWelcome()
        {
            _output.WriteLine();
            int width = GetRuleWidth();

            if (width < 72)
            {
                WriteStyledLine("Welcome to WUM Interactive CLI", Ansi.Accent + Ansi.Bold);
                _output.WriteLine();
                _output.WriteLine("• Run /status to see what Windows Update is doing.");
                _output.WriteLine("• Run /list to browse available updates.");
                _output.WriteLine("• Run /help for a list of all commands.");
                _output.WriteLine();
                return;
            }

            int innerWidth = width - 4;
            int leftWidth = 34;
            int gap = 4;
            int rightWidth = innerWidth - leftWidth - gap;

            WriteStyledLine("╭" + new string('─', width - 2) + "╮", Ansi.Rule);

            PrintFullBorderedLine(new List<(string, string)> { ("Welcome back!", Ansi.Bold + Ansi.Accent) }, innerWidth);
            PrintFullBorderedLine(new List<(string, string)> { ($"WUM v{GetDisplayVersion()}", Ansi.Muted) }, innerWidth);
            PrintFullBorderedLine(new List<(string, string)> { ("", Ansi.Text) }, innerWidth);

            string[] logo = new string[]
            {
                "  ██╗    ██╗██╗   ██╗███╗   ███╗",
                "  ██║    ██║██║   ██║████╗ ████║",
                "  ██║ █╗ ██║██║   ██║██╔████╔██║",
                "  ██║███╗██║██║   ██║██║╚██╔╝██║",
                "  ╚███╔███╔╝╚██████╔╝██║ ╚═╝ ██║",
                "   ╚══╝╚══╝  ╚═════╝ ╚═╝     ╚═╝"
            };

            PrintBorderedLine(logo[0], Ansi.Accent, new List<(string, string)> { ("Tips for getting started:", Ansi.Bold + Ansi.Accent) }, leftWidth, gap, rightWidth);
            PrintBorderedLine(logo[1], Ansi.Accent, new List<(string, string)> { ("• Run ", Ansi.Text), ("/status", Ansi.Accent), (" to see what Windows Update is doing.", Ansi.Text) }, leftWidth, gap, rightWidth);
            PrintBorderedLine(logo[2], Ansi.Accent, new List<(string, string)> { ("• Run ", Ansi.Text), ("/list", Ansi.Accent), (" to browse available updates.", Ansi.Text) }, leftWidth, gap, rightWidth);
            PrintBorderedLine(logo[3], Ansi.Accent, new List<(string, string)> { ("• Run ", Ansi.Text), ("/help", Ansi.Accent), (" for a list of all commands.", Ansi.Text) }, leftWidth, gap, rightWidth);
            PrintBorderedLine(logo[4], Ansi.Accent, new List<(string, string)> { ("", Ansi.Text) }, leftWidth, gap, rightWidth);
            PrintBorderedLine(logo[5], Ansi.Accent, new List<(string, string)> { ("What's new:", Ansi.Bold + Ansi.Accent) }, leftWidth, gap, rightWidth);

            PrintBorderedLine("", Ansi.Text, new List<(string, string)> { ("• Interactive mode with smart suggestions.", Ansi.Muted) }, leftWidth, gap, rightWidth);
            PrintBorderedLine("", Ansi.Text, new List<(string, string)> { ("• Press Tab to complete, ↑/↓ for history.", Ansi.Muted) }, leftWidth, gap, rightWidth);

            WriteStyledLine("╰" + new string('─', width - 2) + "╯", Ansi.Rule);
            _output.WriteLine();
        }

        private void PrintBorderedLine(string left, string leftColor, List<(string Text, string Color)> rightSegments, int leftWidth, int gap, int rightWidth)
        {
            WriteStyled("│ ", Ansi.Rule);
            
            string leftPadded = left.Length > leftWidth ? left.Substring(0, leftWidth) : left.PadRight(leftWidth);
            WriteStyled(leftPadded, leftColor);
            
            WriteStyled(new string(' ', gap), Ansi.Text);
            
            int rightLen = 0;
            foreach (var seg in rightSegments)
            {
                int spaceLeft = rightWidth - rightLen;
                if (spaceLeft <= 0) break;
                string text = seg.Text.Length > spaceLeft ? seg.Text.Substring(0, spaceLeft) : seg.Text;
                WriteStyled(text, seg.Color);
                rightLen += text.Length;
            }
            if (rightLen < rightWidth)
                WriteStyled(new string(' ', rightWidth - rightLen), Ansi.Text);

            WriteStyledLine(" │", Ansi.Rule);
        }

        private void PrintFullBorderedLine(List<(string Text, string Color)> segments, int totalInnerWidth)
        {
            WriteStyled("│ ", Ansi.Rule);
            int len = 0;
            foreach (var seg in segments)
            {
                int spaceLeft = totalInnerWidth - len;
                if (spaceLeft <= 0) break;
                string text = seg.Text.Length > spaceLeft ? seg.Text.Substring(0, spaceLeft) : seg.Text;
                WriteStyled(text, seg.Color);
                len += text.Length;
            }
            if (len < totalInnerWidth)
                WriteStyled(new string(' ', totalInnerWidth - len), Ansi.Text);

            WriteStyledLine(" │", Ansi.Rule);
        }

        private void PrintHelp(string line = "")
        {
            string topic = GetHelpTopic(line);
            if (topic.Length > 0 && PrintCommandSpecificHelp(topic)) return;

            WriteStyledLine("WUM interactive mode", Ansi.AccentBright + Ansi.Bold);
            _output.WriteLine();
            WriteSection("How it works");
            _output.WriteLine("  Type a slash command, then press Enter. Tab completes,");
            _output.WriteLine("  ↑/↓ browses history, and Ctrl+L clears the screen.");
            _output.WriteLine();
            WriteSection("Commands");
            PrintCommandGroups(false);
            _output.WriteLine();
            WriteSection("Session");
            WriteCommandHelp("/commands", "Browse all commands grouped by task");
            WriteCommandHelp("/keys",     "See keyboard shortcuts");
            WriteCommandHelp("/help",     "Show this help");
            WriteCommandHelp("/clear",    "Clear the screen");
            WriteCommandHelp("/exit",     "Leave interactive mode");
            _output.WriteLine();
            PrintKeyHelp();
            _output.WriteLine();
            WriteSection("Examples");
            WriteExample("/status");
            WriteExample("/list --installed");
            WriteExample("/install --all --dry-run");
            _output.WriteLine();
        }

        private void PrintCommandPalette()
        {
            WriteStyledLine("Command palette", Ansi.AccentBright + Ansi.Bold);
            _output.WriteLine();
            PrintCommandGroups(true);
            WriteSection("Tip");
            _output.WriteLine("  Type /help <command> for command-specific options and examples.");
            _output.WriteLine();
        }

        private void PrintCommandGroups(bool includeSession)
        {
            foreach (CommandGroup group in CommandGroups)
            {
                if (!includeSession && group.Label.Equals("This session", StringComparison.OrdinalIgnoreCase)) continue;
                WriteStyled("  " + group.Glyph + "  ", Ansi.AccentBright);
                WriteStyledLine(group.Label, Ansi.Bold + Ansi.AccentBright);
                _output.WriteLine();
                foreach (string command in group.Commands)
                {
                    CommandSuggestion item = CommandCatalog.First(c => c.Command.Equals(command, StringComparison.OrdinalIgnoreCase));
                    WriteCommandHelp("  " + item.Usage, item.Description);
                }
                _output.WriteLine();
            }
        }

        private void PrintKeyHelp()
        {
            WriteSection("Keyboard shortcuts");
            WriteCommandHelp("↑ / ↓",              "Browse history (at an empty prompt) or pick a suggestion");
            WriteCommandHelp("Tab / Shift+Tab",    "Complete the current suggestion (cycle backwards with Shift)");
            WriteCommandHelp("→ at end of line",   "Accept the ghost-text completion");
            WriteCommandHelp("Ctrl+A / Ctrl+E",    "Jump to start / end of line");
            WriteCommandHelp("Ctrl+U / Ctrl+K",    "Delete before / after cursor");
            WriteCommandHelp("Ctrl+W",             "Delete the previous word");
            WriteCommandHelp("Ctrl+L",             "Clear the screen");
            WriteCommandHelp("Ctrl+C",             "Cancel the current line (press twice to exit)");
            WriteCommandHelp("Ctrl+D (empty)",     "Exit interactive mode");
        }

        private bool PrintCommandSpecificHelp(string topic)
        {
            if (!CommandSpecByName.TryGetValue(topic, out CommandSpec? spec)) return false;
            WriteStyledLine("/" + spec.Name, Ansi.AccentBright + Ansi.Bold);
            _output.WriteLine();
            WriteSection("Usage");
            _output.WriteLine("  " + spec.Usage);
            _output.WriteLine();
            _output.WriteLine("  " + spec.Description);
            if (spec.Subcommands.Length > 0) { _output.WriteLine(); WriteSection("Subcommands"); foreach (SubcommandSpec sub in spec.Subcommands) WriteCommandHelp(sub.Usage, sub.Description); }
            if (spec.Options.Length > 0) { _output.WriteLine(); WriteSection("Options"); foreach (OptionSpec option in spec.Options) WriteCommandHelp(option.Usage, option.Description); }
            _output.WriteLine();
            return true;
        }

        private void PrintSlashHint()
        {
            WriteStyled("Hint: ", Ansi.Warn);
            _output.WriteLine("Commands here start with /.");
            _output.Write("      Try ");
            WriteStyled("/list", Ansi.Accent);
            _output.Write(" or type ");
            WriteStyled("/help", Ansi.Accent);
            _output.WriteLine(" to see everything.");
        }

        private void PrintUnknownCommand(string command)
        {
            WriteStyled("Hmm — ", Ansi.Warn);
            _output.WriteLine("\"" + command + "\" isn't a WUM command.");
            string? suggestion = FindSuggestion(command);
            if (suggestion is not null)
            {
                _output.Write("       Did you mean ");
                WriteStyled(suggestion, Ansi.Accent);
                _output.WriteLine("?");
            }
            _output.Write("       Type ");
            WriteStyled("/commands", Ansi.Accent);
            _output.WriteLine(" to browse, or /help for a quick tour.");
        }

        internal static IReadOnlyList<CommandSuggestion> GetCommandSuggestions(string input) => GetCommandSuggestions(input, input.Length);

        internal static IReadOnlyList<CommandSuggestion> GetCommandSuggestions(string input, int cursor)
        {
            if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("/", StringComparison.Ordinal)) return Array.Empty<CommandSuggestion>();
            cursor = Math.Clamp(cursor, 0, input.Length);
            string beforeCursor = input.Substring(0, cursor);
            int replaceStart = FindCurrentTokenStart(beforeCursor);
            string currentToken = beforeCursor.Substring(replaceStart);
            string trimmedBeforeCursor = beforeCursor.TrimStart();

            if (trimmedBeforeCursor == "/" || !beforeCursor.Substring(0, replaceStart).Any(char.IsWhiteSpace))
                return BuildCommandSuggestions(currentToken, replaceStart, cursor - replaceStart);

            var tokens = SplitForCompletion(beforeCursor);
            if (tokens.Count == 0) return Array.Empty<CommandSuggestion>();
            string slashCommand = tokens[0];
            if (!slashCommand.StartsWith("/", StringComparison.Ordinal)) return Array.Empty<CommandSuggestion>();

            string commandName = slashCommand.TrimStart('/');
            if (!CommandSpecByName.TryGetValue(commandName, out CommandSpec? spec)) return Array.Empty<CommandSuggestion>();

            bool isNewToken = beforeCursor.Length > 0 && char.IsWhiteSpace(beforeCursor[beforeCursor.Length - 1]);
            bool completingOption = currentToken.StartsWith("-", StringComparison.Ordinal);
            string? subcommand = FindKnownSubcommand(spec, tokens.Skip(1));

            if (completingOption)
            {
                var optionScope = subcommand is null ? spec.Options : spec.Subcommands.First(s => s.Name.Equals(subcommand, StringComparison.OrdinalIgnoreCase)).Options;
                return BuildOptionSuggestions(optionScope, currentToken, replaceStart, cursor - replaceStart);
            }

            if (subcommand is null && spec.Subcommands.Length > 0)
            {
                var suggestions = BuildSubcommandSuggestions(spec, currentToken, replaceStart, cursor - replaceStart).ToList();
                if (isNewToken && spec.Options.Length > 0) suggestions.AddRange(BuildOptionSuggestions(spec.Options, string.Empty, cursor, 0));
                return suggestions.Take(MaxVisibleSuggestions).ToArray();
            }

            if (isNewToken)
            {
                var optionScope = subcommand is null ? spec.Options : spec.Subcommands.First(s => s.Name.Equals(subcommand, StringComparison.OrdinalIgnoreCase)).Options;
                return BuildOptionSuggestions(optionScope, string.Empty, cursor, 0);
            }

            return Array.Empty<CommandSuggestion>();
        }

        internal static string GetGhostCompletion(string input)
        {
            var suggestions = GetCommandSuggestions(input, input.Length);
            return GetGhostCompletion(input, input.Length, suggestions, 0);
        }

        private static string GetGhostCompletion(string buffer, int cursor, IReadOnlyList<CommandSuggestion> suggestions, int selectedIndex)
        {
            if (cursor != buffer.Length || suggestions.Count == 0) return string.Empty;
            selectedIndex = Math.Clamp(selectedIndex, 0, suggestions.Count - 1);
            CommandSuggestion suggestion = suggestions[selectedIndex];
            if (suggestion.ReplaceStart < 0 || suggestion.ReplaceStart > cursor) return string.Empty;

            string prefix = buffer.Substring(suggestion.ReplaceStart, cursor - suggestion.ReplaceStart);
            string insertText = suggestion.InsertText;

            return insertText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && insertText.Length > prefix.Length
                ? insertText.Substring(prefix.Length) : string.Empty;
        }

        private static IReadOnlyList<CommandSuggestion> BuildCommandSuggestions(string prefix, int replaceStart, int replaceLength) =>
            CommandCatalog.Where(c => c.Command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(c => new CommandSuggestion(c.Command, c.Usage, c.Description, c.Command, replaceStart, replaceLength, IsSessionCommand(c.Command) ? SuggestionKind.Session : SuggestionKind.Command))
                .Take(MaxVisibleSuggestions).ToArray();

        private static IEnumerable<CommandSuggestion> BuildSubcommandSuggestions(CommandSpec spec, string prefix, int replaceStart, int replaceLength) =>
            spec.Subcommands.Where(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(s => new CommandSuggestion(s.Name, s.Usage, s.Description, s.Name, replaceStart, replaceLength, SuggestionKind.Subcommand));

        private static IReadOnlyList<CommandSuggestion> BuildOptionSuggestions(IReadOnlyList<OptionSpec> options, string prefix, int replaceStart, int replaceLength)
        {
            if (options.Count == 0) return Array.Empty<CommandSuggestion>();
            var suggestions = new List<CommandSuggestion>();
            foreach (OptionSpec option in options)
                foreach (string name in option.Names)
                {
                    if (!string.IsNullOrEmpty(prefix) && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    suggestions.Add(new CommandSuggestion(name, option.Usage, option.Description, name, replaceStart, replaceLength, SuggestionKind.Option));
                }
            return suggestions.GroupBy(s => s.Command, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(MaxVisibleSuggestions).ToArray();
        }

        private static string? FindKnownSubcommand(CommandSpec spec, IEnumerable<string> tokens)
        {
            foreach (string token in tokens)
            {
                var sub = spec.Subcommands.FirstOrDefault(s => s.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
                if (sub is not null) return sub.Name;
            }
            return null;
        }

        private static int FindCurrentTokenStart(string input)
        {
            for (int i = input.Length - 1; i >= 0; i--) if (char.IsWhiteSpace(input[i])) return i + 1;
            return 0;
        }

        private static List<string> SplitForCompletion(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return tokens;
            var current = new StringBuilder();
            char quote = '\0';
            foreach (char c in input)
            {
                if (quote != '\0') { if (c == quote) { quote = '\0'; continue; } current.Append(c); continue; }
                if (c == '"' || c == '\'') { quote = c; continue; }
                if (char.IsWhiteSpace(c)) { if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); } continue; }
                current.Append(c);
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        private static bool IsSessionCommand(string command) =>
            command.Equals("/help", StringComparison.OrdinalIgnoreCase) || command.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("/commands", StringComparison.OrdinalIgnoreCase) || command.Equals("/keys", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("/exit", StringComparison.OrdinalIgnoreCase) || command.Equals("/quit", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("/clear", StringComparison.OrdinalIgnoreCase) || command.Equals("/version", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("/info", StringComparison.OrdinalIgnoreCase);

        private static string GetSuggestionPanelTitle(IReadOnlyList<CommandSuggestion> suggestions)
        {
            if (suggestions.Count == 0) return "matches";
            return suggestions[0].Kind switch
            {
                SuggestionKind.Command    => "commands",
                SuggestionKind.Session    => "session",
                SuggestionKind.Subcommand => "subcommands",
                SuggestionKind.Option     => "options",
                SuggestionKind.Value      => "values",
                _ => "matches"
            };
        }

        private void WriteRuleWithLabel(string label)
        {
            string prefix = "─ " + label + " ";
            int fill = Math.Max(0, GetRuleWidth() - prefix.Length);
            WriteStyled("─ ", Ansi.Rule);
            WriteStyled(label, Ansi.Bold + Ansi.Muted);
            WriteStyledLine(" " + new string('─', fill), Ansi.Rule);
        }

        private int BeginEditorFrame()
        {
            int readyTop = GetSafeCursorTop();
            int shift = ReserveVisibleRows(readyTop, 4); // 1 ready + 1 prompt + 1 hints + 1 status
            readyTop -= shift;
            if (readyTop < 0) readyTop = 0;

            Console.SetCursorPosition(0, readyTop);
            WriteRuleWithLabel("ready");
            return GetSafeCursorTop();
        }

        private string GetSessionStatus()
        {
            string role = IsRunningElevatedSafely() ? "admin" : "standard";
            string editor = _useKeyEditor ? "smart editor" : "plain input";
            return $"{role} · {editor} · history {_history.Count} · {Truncate(Environment.CurrentDirectory, 42)}";
        }

        private static bool IsRunningElevatedSafely()
        {
            try { return WUM.CLI.Helpers.AdminHelper.IsRunningAsAdmin(); }
            catch { return false; }
        }

        private void PrintResponseHeader(string label)
        {
            _output.WriteLine();
            WriteStyled("● ", Ansi.Accent);
            WriteStyled(label, Ansi.Bold + Ansi.Text);
            int used = 2 + label.Length + 1;
            int fill = Math.Max(1, GetRuleWidth() - used);
            WriteStyledLine(" " + new string('─', fill), Ansi.Rule);
        }

        private void PrintResponseFooter(int exitCode)
        {
            string status = exitCode == 0 ? "✓ done" : "✗ failed (exit " + exitCode + ")";
            WriteStyled("  ", Ansi.Muted);
            WriteStyledLine(status, exitCode == 0 ? Ansi.Ok : Ansi.Warn);
        }

        private void WriteSection(string text) => WriteStyledLine(text + ":", Ansi.Bold + Ansi.Accent);
        private void WriteCommandHelp(string command, string description) { _output.Write("  "); WriteStyled(command.PadRight(33), Ansi.Accent); _output.WriteLine(description); }
        private void WriteExample(string command) { _output.Write("  "); WriteStyledLine(command, Ansi.Dim + Ansi.White); }
        private void WriteStyled(string text, string style) { if (_useAnsi) _output.Write(style); _output.Write(text); if (_useAnsi) _output.Write(Ansi.Reset); }
        private void WriteStyledLine(string text, string style) { WriteStyled(text, style); _output.WriteLine(); }

        private static string GetDisplayVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "unknown";
            int plus = version.IndexOf('+');
            return plus >= 0 ? version.Substring(0, plus) : version;
        }

        private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : (maxLength <= 1 ? text.Substring(0, maxLength) : text.Substring(0, maxLength - 1) + "…");
        private static int GetRuleWidth() { if (Console.IsOutputRedirected) return 96; try { return Math.Clamp(Console.WindowWidth - 1, 72, 140); } catch { return 96; } }
        private static int GetSafeCursorTop() { try { return ClampCursorTop(Console.CursorTop); } catch { return 0; } }
        private static int ClampCursorTop(int top) => Math.Clamp(top, 0, GetBufferHeightSafely() - 1);
        private static int GetBufferHeightSafely() { try { return Math.Max(1, Console.BufferHeight); } catch { return 1; } }
        private static int GetEditorRowsForSuggestions(int suggestionCount) => 1 + (suggestionCount > 0 ? suggestionCount + 2 : 0) + 2;

        private static int ReserveVisibleRows(int top, int rows)
        {
            try
            {
                if (Console.IsOutputRedirected) return 0;
                int neededBottom = top + rows - 1;
                int visibleBottom = Console.WindowTop + Console.WindowHeight - 1;

                if (neededBottom > visibleBottom)
                {
                    int scrollAmount = neededBottom - visibleBottom;
                    Console.SetCursorPosition(0, visibleBottom);
                    int beforeTop = Console.CursorTop;
                    for (int i = 0; i < scrollAmount; i++)
                        Console.WriteLine();

                    int afterTop = Console.CursorTop;
                    // If the terminal buffer is fixed (like Linux or Windows Terminal), 
                    // writing newlines shifts the buffer up. We calculate the shift to adjust coordinates.
                    int shift = (beforeTop + scrollAmount) - afterTop;
                    return shift;
                }
            }
            catch { }
            return 0;
        }

        private static bool TrySetTreatControlCAsInput(bool value, out bool previous) { previous = false; try { previous = Console.TreatControlCAsInput; Console.TreatControlCAsInput = value; return true; } catch { return false; } }

        private void NavigateHistory(StringBuilder buffer, ref int cursor, int delta, ref int historyIndex, ref string draftBeforeHistory)
        {
            if (_history.Count == 0) return;
            if (historyIndex == _history.Count) draftBeforeHistory = buffer.ToString();
            historyIndex = Math.Clamp(historyIndex + delta, 0, _history.Count);
            buffer.Clear();
            buffer.Append(historyIndex == _history.Count ? draftBeforeHistory : _history[historyIndex]);
            cursor = buffer.Length;
        }

        private bool ShouldNavigateHistory(string buffer, int historyIndex) => _history.Count > 0 && (buffer.Length == 0 || historyIndex < _history.Count);
        private void ResetHistory(ref int historyIndex, ref string draftBeforeHistory) { historyIndex = _history.Count; draftBeforeHistory = string.Empty; }
        private static void DeleteWordBeforeCursor(StringBuilder buffer, ref int cursor) { if (cursor <= 0) return; int end = cursor; while (cursor > 0 && char.IsWhiteSpace(buffer[cursor - 1])) cursor--; while (cursor > 0 && !char.IsWhiteSpace(buffer[cursor - 1])) cursor--; buffer.Remove(cursor, end - cursor); }
        private static void MoveWordLeft(string buffer, ref int cursor) { while (cursor > 0 && char.IsWhiteSpace(buffer[cursor - 1])) cursor--; while (cursor > 0 && !char.IsWhiteSpace(buffer[cursor - 1])) cursor--; }
        private static void MoveWordRight(string buffer, ref int cursor) { while (cursor < buffer.Length && !char.IsWhiteSpace(buffer[cursor])) cursor++; while (cursor < buffer.Length && char.IsWhiteSpace(buffer[cursor])) cursor++; }
        private void AddHistoryEntry(string line) { if (string.IsNullOrWhiteSpace(line)) return; if (_history.Count > 0 && _history[_history.Count - 1].Equals(line, StringComparison.Ordinal)) return; _history.Add(line); if (_history.Count > MaxHistoryEntries) _history.RemoveRange(0, _history.Count - MaxHistoryEntries); SaveHistory(); }
        private void LoadHistory() { try { string path = GetHistoryPath(); if (!File.Exists(path)) return; foreach (string line in File.ReadAllLines(path)) { string trimmed = line.Trim(); if (trimmed.Length > 0 && !IsExitCommand(trimmed)) _history.Add(trimmed); } if (_history.Count > MaxHistoryEntries) _history.RemoveRange(0, _history.Count - MaxHistoryEntries); } catch { } }
        private void SaveHistory() { try { string path = GetHistoryPath(); string? dir = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); File.WriteAllLines(path, _history); } catch { } }
        private static string GetHistoryPath() { string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); if (string.IsNullOrWhiteSpace(baseDir)) baseDir = Path.GetTempPath(); return Path.Combine(baseDir, "WUM", "interactive-history.txt"); }

        private static bool ShouldCompleteOnEnter(string buffer, IReadOnlyList<CommandSuggestion> suggestions)
        {
            if (suggestions.Count == 0) return false;
            if (buffer.Any(char.IsWhiteSpace)) return false;
            string command = GetCommandWord(buffer.Trim());
            return command == "/" || !KnownCommandSet.Contains(command);
        }

        private static void CompleteSuggestion(StringBuilder buffer, ref int cursor, IReadOnlyList<CommandSuggestion> suggestions, int selectedIndex)
        {
            if (suggestions.Count == 0) return;
            selectedIndex = Math.Clamp(selectedIndex, 0, suggestions.Count - 1);
            CommandSuggestion suggestion = suggestions[selectedIndex];
            int start = Math.Clamp(suggestion.ReplaceStart, 0, buffer.Length);
            int length = Math.Clamp(suggestion.ReplaceLength, 0, buffer.Length - start);
            buffer.Remove(start, length);
            buffer.Insert(start, suggestion.InsertText);
            cursor = start + suggestion.InsertText.Length;
            if (cursor == buffer.Length && (buffer.Length == 0 || !char.IsWhiteSpace(buffer[buffer.Length - 1]))) { buffer.Append(' '); cursor++; }
        }

        private static int ClampSelectionIndex(string buffer, int selectedIndex) => ClampSelectionIndex(buffer, buffer.Length, selectedIndex);
        private static int ClampSelectionIndex(string buffer, int cursor, int selectedIndex)
        {
            var suggestions = GetCommandSuggestions(buffer, cursor);
            return suggestions.Count == 0 ? 0 : Math.Clamp(selectedIndex, 0, suggestions.Count - 1);
        }

        private static bool IsHelpCommand(string command) => command.Equals("/help", StringComparison.OrdinalIgnoreCase) || command.Equals("/?", StringComparison.OrdinalIgnoreCase);
        private static bool IsExitCommand(string line) => line.Equals("/exit", StringComparison.OrdinalIgnoreCase) || line.Equals("/quit", StringComparison.OrdinalIgnoreCase) || line.Equals("exit", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase);
        private static string GetCommandWord(string line) { int end = line.IndexOfAny(new[] { ' ', '\t' }); return end < 0 ? line : line.Substring(0, end); }
        private static string GetHelpTopic(string line) { if (string.IsNullOrWhiteSpace(line)) return string.Empty; string[] parts; try { parts = CommandLineTokenizer.Tokenize(line); } catch { return string.Empty; } if (parts.Length < 2) return string.Empty; return parts[1].TrimStart('/'); }
        private static string? FindSuggestion(string command) { string target = command.TrimStart('/'); string? best = null; int bestDistance = int.MaxValue; foreach (string candidate in CommandCatalog.Select(c => c.Command).Where(c => c != "/?")) { string candidateText = candidate.TrimStart('/'); int distance = LevenshteinDistance(target, candidateText); if (distance < bestDistance) { bestDistance = distance; best = candidate; } } return bestDistance <= 2 ? best : null; }
        private static int LevenshteinDistance(string left, string right) { if (left.Length == 0) return right.Length; if (right.Length == 0) return left.Length; var costs = new int[right.Length + 1]; for (int i = 0; i <= right.Length; i++) costs[i] = i; for (int i = 1; i <= left.Length; i++) { int previous = costs[0]; costs[0] = i; for (int j = 1; j <= right.Length; j++) { int current = costs[j]; int insert = costs[j] + 1; int delete = costs[j - 1] + 1; int replace = previous + (left[i - 1] == right[j - 1] ? 0 : 1); costs[j] = Math.Min(Math.Min(insert, delete), replace); previous = current; } } return costs[right.Length]; }
        private static void ClearConsoleSafely()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    Console.Write("\u001b[2J\u001b[3J\u001b[H");
                    Console.Out.Flush();
                }
                Console.Clear();
            }
            catch { }
        }
        private static bool CanUseKeyEditor() { try { return !Console.IsInputRedirected && !Console.IsOutputRedirected && Console.In is not StringReader && Console.Out is not StringWriter; } catch { return false; } }

        private readonly struct LineResult
        {
            private LineResult(bool shouldExit, int exitCode) { ShouldExit = shouldExit; ExitCode = exitCode; }
            public bool ShouldExit { get; }
            public int ExitCode { get; }
            public static LineResult Continue(int exitCode) => new(false, exitCode);
            public static LineResult Exit(int exitCode) => new(true, exitCode);
        }

        // Windows 11 Fluent Dark Theme Colors
        private static class Ansi
        {
            public const string Reset = "\u001b[0m";
            public const string Bold = "\u001b[1m";
            public const string Dim = "\u001b[2m";
            public const string Accent = "\u001b[38;2;76;194;255m";        // Fluent Blue
            public const string AccentBright = "\u001b[38;2;160;216;255m";  // Lighter Fluent Blue
            public const string Ok = "\u001b[38;2;125;214;107m";            // Fluent Green
            public const string Warn = "\u001b[38;2;255;214;68m";           // Fluent Yellow
            public const string Error = "\u001b[38;2;255;107;107m";         // Fluent Red
            public const string Text = "\u001b[38;2;255;255;255m";          // Pure White
            public const string Muted = "\u001b[38;2;153;153;153m";         // Fluent Gray
            public const string Ghost = "\u001b[38;2;102;102;102m";         // Darker Gray
            public const string Rule = "\u001b[38;2;51;51;51m";             // Dark Gray for borders
            public const string Green = Ok;
            public const string Red = Error;
            public const string Yellow = Warn;
            public const string White = Text;
            public const string Cyan = AccentBright;
        }
    }
}