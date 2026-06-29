# Plan: Add WUM Interactive Slash Command Mode

## Goal

Add a polished interactive shell that starts when `wum` is launched with no arguments. Normal one-shot CLI behavior must remain unchanged. Interactive commands must reuse the existing `System.CommandLine` command tree and command handlers.

This is not an AI assistant feature. No model calls, chat behavior, agent behavior, or code generation features.

## Current Code Observations

- `src/WUM.CLI/Program.cs` currently does all startup work in `Main`:
  - UTF-8 console setup
  - `--info` short-circuit
  - Serilog setup
  - DI container creation
  - root command construction
  - command registration
  - parser pipeline creation
  - parser invocation
- Command classes already expose `Build()` methods returning `System.CommandLine.Command`.
- Existing command behavior should be reused by invoking the same parser from interactive mode.
- `PrintDeveloperInfo()` is private in `Program.cs`; interactive `/info` needs a reusable path to that same output.
- Tests currently cover some command/service behavior, but there is no parser or REPL test surface yet.

## Design Decisions

1. Refactor startup, do not rewrite the CLI.
   - Keep all existing command classes.
   - Move command registration into reusable `BuildRootCommand(IServiceProvider services)`.
   - Move parser creation into reusable `BuildParser(RootCommand root)`.
   - Keep `--info` short-circuit behavior for normal CLI mode because it exists today.

2. Add `InteractiveShell`.
   - Owns the REPL loop.
   - Prints the welcome screen.
   - Reads input until `/exit`, `/quit`, `exit`, or `quit`.
   - Handles interactive-only commands.
   - Converts slash commands into normal parser args.
   - Calls the existing parser for real WUM commands.

3. Add `CommandLineTokenizer`.
   - No naive `Split(' ')`.
   - Support whitespace-delimited args.
   - Support quoted args, especially:
     - `/search "security update"`
     - `/settings set active-hours 9-18`
     - `/install KB5034441 KB5035853 --dry-run`
   - Report friendly tokenizer errors such as unmatched quotes.

4. Keep admin behavior per command.
   - Do not require elevation to enter interactive shell.
   - Existing modifying commands keep their current `RequireAdmin()` checks.

5. Keep output behavior.
   - Normal command output remains owned by existing command handlers.
   - Interactive shell adds only prompt, welcome, help, unknown command messages, and session commands.

## Implementation Steps

### 1. Refactor `Program.cs`

- Extract:
  - `SetupConsoleEncoding()`
  - `ConfigureLogging()`
  - `BuildServices()`
  - `BuildRootCommand(IServiceProvider services)`
  - `BuildParser(RootCommand root)`
  - `PrintDeveloperInfo()`
- Keep normal flow:
  - if `args.Any(a => a == "--info")`, print developer info and return `0`
  - if `args.Length > 0`, invoke parser normally
  - if `args.Length == 0`, run interactive shell
- Make only what tests need `internal`, with `InternalsVisibleTo("WUM.CLI.Tests")` if needed.
- Ensure `Log.CloseAndFlushAsync()` runs after both normal and interactive paths.

### 2. Add `InteractiveShell.cs`

Responsibilities:

- Print:

```text
WUM interactive mode
Windows Update Manager CLI

Type /help to see commands.
Type /exit to quit.

wum>
```

- Loop:
  - read line
  - trim whitespace
  - ignore empty lines
  - dispatch built-ins
  - reject non-slash input with hint, except bare `exit` and `quit`
  - forward slash commands to parser

Built-ins:

- `/help` and `/?`: print interactive help
- `/exit`, `/quit`, `exit`, `quit`: print `Goodbye.` and return
- `/clear`: clear terminal, guarded so redirected output does not crash
- `/version`: invoke parser with `--version`
- `/info`: call the same developer info method used by normal `wum --info`

Forwarded commands:

- Remove leading `/`
- Tokenize remaining input
- Invoke parser with resulting args
- Example: `/settings set active-hours 9-18` becomes `["settings", "set", "active-hours", "9-18"]`

### 3. Add Unknown Command Suggestions

Known slash commands:

- `/status`
- `/list`
- `/search`
- `/install`
- `/uninstall`
- `/hide`
- `/history`
- `/pause`
- `/schedule`
- `/settings`
- `/reboot`
- `/diagnose`
- `/help`
- `/exit`
- `/quit`
- `/clear`
- `/version`
- `/info`
- `/?`

Behavior:

```text
Unknown command: /lst
Did you mean /list?
Type /help to see available commands.
```

Use a small Levenshtein-distance helper or prefix similarity helper. Keep threshold conservative so bad suggestions do not look silly.

### 4. Add Interactive Help Text

Create a single method, probably `PrintHelp()`, matching the requested help shape:

- usage
- command list
- session command list
- examples

Keep it independent from normal `System.CommandLine` help. Do not replace `wum --help` or command-specific help.

### 5. Add `CommandLineTokenizer.cs`

Tokenizer behavior:

- Whitespace separates args outside quotes.
- Double quotes group text.
- Single quotes can also group text if easy to support.
- Backslash can escape quotes inside quoted text.
- Quotes are removed from final tokens.
- Empty quoted string becomes an empty token.
- Unmatched quote throws a `FormatException` with a short clear message.

Examples:

- `list --installed --json` -> `["list", "--installed", "--json"]`
- `search "security update"` -> `["search", "security update"]`
- `settings set active-hours 9-18` -> `["settings", "set", "active-hours", "9-18"]`
- `install KB5034441 KB5035853 --dry-run` -> `["install", "KB5034441", "KB5035853", "--dry-run"]`

### 6. Test Strategy

Add focused tests under `tests/WUM.CLI.Tests`, likely:

- `InteractiveShellTests.cs`
- `CommandLineTokenizerTests.cs`

Prefer testing shell routing without running real Windows Update operations:

- Inject a fake command invoker into `InteractiveShell` or wrap parser invocation behind a small delegate.
- Feed input through `StringReader`.
- Capture output through `StringWriter`.
- Use a no-op clear action for `/clear`.
- Use a fake info action for `/info`.

Minimum tests:

1. no-args interactive path can show welcome and exit cleanly by running shell with `/exit`
2. `/list` records args `["list"]`
3. `/list --installed` records args `["list", "--installed"]`
4. `/settings set active-hours 9-18` records args `["settings", "set", "active-hours", "9-18"]`
5. `/help`, `/?`, `/clear`, `/version`, `/info`, `exit`, and `/quit` are handled without forwarding to WUM commands
6. `/lst` prints unknown command plus `/list` suggestion
7. non-slash input like `list --installed` prints the slash-command hint
8. tokenizer preserves quoted values
9. tokenizer reports unmatched quotes
10. existing parser still recognizes one-shot commands after refactor, using parser-level smoke tests where safe

Avoid tests that call real WUA COM services. Keep command execution tests at parser construction or shell mapping level unless services are mocked.

### 7. Documentation Touches

After implementation, update docs only where useful:

- `README.md`: add short interactive mode example near usage.
- `docs/commands.md` or `docs/use-wum.md`: add slash command mode reference.

Keep docs concise. Do not let docs work distract from the CLI behavior.

### 8. Verification Commands

Run:

```powershell
dotnet build
dotnet test
```

Manual smoke checks:

```powershell
dotnet run --project src/WUM.CLI --
dotnet run --project src/WUM.CLI -- --help
dotnet run --project src/WUM.CLI -- --version
dotnet run --project src/WUM.CLI -- --info
dotnet run --project src/WUM.CLI -- list --help
```

Inside interactive mode smoke:

```text
/help
/version
/info
/list --installed
/search "security update"
/settings show
/lst
list --installed
/exit
```

## Risk Notes

- `System.CommandLine` beta4 behavior must stay intact. Parser creation should stay as close as possible to current `Program.cs`.
- `--info` is currently handled before parser invocation. Preserve this to avoid behavior drift.
- Commands write directly to `Console`, so shell tests should focus on routing and built-ins instead of running real command handlers.
- `Console.Clear()` can throw in redirected sessions. Guard it.
- Interactive loop must not swallow parser errors silently. Let existing exception handler render errors.
- Avoid changing admin logic. Interactive mode should be just another way to reach the existing commands.

## Done Criteria

- `wum` with no args opens interactive shell.
- Slash commands map to existing command handlers.
- Built-ins work: `/help`, `/?`, `/exit`, `/quit`, `/clear`, `/version`, `/info`.
- Bare `exit` and `quit` exit for usability.
- Unknown commands show helpful suggestion.
- Non-slash commands show slash hint.
- Quoted arguments parse correctly.
- Existing one-shot commands still work.
- Tests cover tokenizer and shell mapping.
- Build and test pass.
