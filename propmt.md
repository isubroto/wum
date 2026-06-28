# Prompt: Add Claude Code-Style Interactive Command Mode to WUM CLI

You are an expert .NET CLI engineer.

The project is **WUM - Windows Update Manager CLI**. It is a .NET 10 Windows CLI tool built with `System.CommandLine`. It manages Windows Updates through existing commands such as `status`, `list`, `search`, `install`, `uninstall`, `hide`, `history`, `pause`, `schedule`, `settings`, `reboot`, and `diagnose`.

Important:

This is **not an AI assistant feature**.

Do not add:

- AI chat
- model selection
- code generation
- LLM calls
- agent behavior
- project coding assistant features

The goal is only to improve the CLI interaction style so `wum` can be used like a professional interactive command shell, similar to the command experience of Claude Code.

## Current Problem

Right now, running commands like this works:

```bash
wum status
wum list
wum install --all
wum settings show
wum diagnose
```

But running only:

```bash
wum
```

does not open an interactive command session.

I want bare `wum` to start an interactive shell where all existing CLI commands can be used as slash commands.

## Main Goal

When the user runs:

```bash
wum
```

WUM should open an interactive shell.

Inside that shell, every existing command should work with a slash prefix.

For example:

```bash
wum list
```

should also work inside interactive mode as:

```bash
/list
```

And:

```bash
wum list --installed
```

should work inside interactive mode as:

```bash
/list --installed
```

And:

```bash
wum settings set active-hours 9-18
```

should work inside interactive mode as:

```bash
/settings set active-hours 9-18
```

## Required Command Mapping

The following normal CLI commands must work in both modes.

| Normal CLI command               | Interactive command           |
| -------------------------------- | ----------------------------- |
| `wum status`                     | `/status`                     |
| `wum list`                       | `/list`                       |
| `wum search <query>`             | `/search <query>`             |
| `wum install [options]`          | `/install [options]`          |
| `wum uninstall <KB>`             | `/uninstall <KB>`             |
| `wum hide add <update-id>`       | `/hide add <update-id>`       |
| `wum hide remove <update-id>`    | `/hide remove <update-id>`    |
| `wum hide list`                  | `/hide list`                  |
| `wum history`                    | `/history`                    |
| `wum pause`                      | `/pause`                      |
| `wum pause resume`               | `/pause resume`               |
| `wum schedule`                   | `/schedule`                   |
| `wum schedule show`              | `/schedule show`              |
| `wum schedule set [options]`     | `/schedule set [options]`     |
| `wum schedule clear`             | `/schedule clear`             |
| `wum settings`                   | `/settings`                   |
| `wum settings show`              | `/settings show`              |
| `wum settings set <key> <value>` | `/settings set <key> <value>` |
| `wum settings reset`             | `/settings reset`             |
| `wum reboot [options]`           | `/reboot [options]`           |
| `wum diagnose`                   | `/diagnose`                   |
| `wum --info`                     | `/info`                       |
| `wum --version`                  | `/version`                    |
| `wum --help`                     | `/help`                       |

## Keep Existing CLI Behavior

Do not break existing usage.

These must continue to work exactly as before:

```bash
wum status
wum list --security
wum search KB5034441
wum install --all --dry-run
wum pause --days 14
wum settings show
wum diagnose
wum --help
wum --version
wum --info
```

## Interactive Mode Behavior

When `wum` is launched without arguments, show a clean welcome screen:

```text
WUM interactive mode
Windows Update Manager CLI

Type /help to see commands.
Type /exit to quit.

wum>
```

The prompt should stay open until the user exits:

```text
wum> /status
wum> /list --installed
wum> /settings show
wum> /exit
Goodbye.
```

## Built-In Interactive Commands

Add these interactive-only commands:

```text
/help       Show interactive help
/exit       Exit interactive mode
/quit       Alias for /exit
/clear      Clear the terminal screen
/version    Show WUM version
/info       Show developer/build information
```

Optional but useful:

```text
/?          Alias for /help
```

## Command Parsing Rules

Inside interactive mode:

1. A line starting with `/` is treated as a WUM command.
2. Remove the leading slash.
3. Convert the remaining input into the same argument format used by normal CLI mode.
4. Invoke the existing `System.CommandLine` parser or command handler with those arguments.
5. Do not duplicate command logic.

Example:

```text
/list --installed --json
```

should internally run the same logic as:

```bash
wum list --installed --json
```

Example:

```text
/install KB5034441 --dry-run
```

should internally run the same logic as:

```bash
wum install KB5034441 --dry-run
```

## Important Implementation Requirement

The current project uses command classes such as:

```text
StatusCommand
ListCommand
SearchCommand
InstallCommand
UninstallCommand
HideCommand
HistoryCommand
PauseCommand
ScheduleCommand
SettingsCommand
RebootCommand
DiagnoseCommand
```

Each command already has its own `Build()` method returning a `System.CommandLine.Command`.

Refactor `Program.cs` so command registration can be reused by both:

1. normal one-shot CLI mode
2. interactive shell mode

Recommended structure:

```text
Program.cs
  - Main(args)
  - BuildServices()
  - BuildRootCommand(services)
  - RunInteractiveAsync(parser)
  - PrintDeveloperInfo()

InteractiveShell.cs
  - runs the REPL loop
  - reads user input
  - handles /help, /exit, /clear, /version, /info
  - forwards other slash commands to the existing parser

CommandLineTokenizer.cs, if needed
  - splits interactive input into args safely
  - supports quoted values
```

The exact file names can be changed if another structure fits the repo better, but the command logic must stay shared.

## Argument Tokenization

Interactive commands must support options and quoted arguments.

These should parse correctly:

```text
/search "security update"
/schedule set --day Friday --time 03:00 --auto-install --auto-reboot --all
/settings set active-hours 9-18
/install KB5034441 KB5035853 --dry-run
```

Do not use a naive `Split(' ')` if it breaks quoted strings.

Use a safe tokenizer or a `System.CommandLine`-compatible parsing approach.

## Error Handling

Interactive mode must not crash on invalid input.

If the user enters an unknown command:

```text
wum> /lst
```

show:

```text
Unknown command: /lst
Did you mean /list?
Type /help to see available commands.
```

Add simple typo suggestions for known commands.

Known slash commands should include:

```text
/status
/list
/search
/install
/uninstall
/hide
/history
/pause
/schedule
/settings
/reboot
/diagnose
/help
/exit
/quit
/clear
/version
/info
```

If the user types a command without `/`, show a helpful hint:

```text
Commands in interactive mode start with /.
Try /list or type /help.
```

Optional: also allow non-slash command input like `list --installed`, but slash commands must be the official documented behavior.

## Admin Behavior

Keep the existing admin behavior.

The app should still launch as normal without requiring elevation.

Read-only commands should run without admin:

```text
/status
/list
/search
/history
/version
/info
/help
```

System-changing commands should keep the same admin checks they already use:

```text
/install
/uninstall
/hide
/pause
/schedule set
/settings set
/settings reset
/reboot
```

Do not force the whole interactive shell to run as administrator. Let the existing per-command admin logic handle it.

## Output Behavior

Keep the current WUM output style:

- colored tables
- progress bars
- JSON output when `--json` is passed
- `--no-color` behavior
- verbose output with `-v`
- existing confirmation prompts
- existing error rendering through `ConsoleRenderer`

Interactive mode should not remove or redesign existing command output.

## Help Output

`/help` should show an interactive help screen:

```text
WUM interactive mode

Usage:
  /command [options]

Commands:
  /status                         Show Windows Update status dashboard
  /list [options]                 List available or installed updates
  /search <query>                 Search updates by keyword
  /install [KB...] [options]      Install updates
  /uninstall <KB>                 Uninstall an installed update
  /hide add <update-id>           Hide an update
  /hide remove <update-id>        Unhide an update
  /hide list                      List hidden updates
  /history                        Show update history
  /pause [--days N]               Pause Windows Updates
  /pause resume                   Resume Windows Updates
  /schedule [show|set|clear]      Manage weekly update schedule
  /settings [show|set|reset]      View or change Windows Update settings
  /reboot [options]               Schedule or cancel reboot
  /diagnose                       Run Windows Update diagnostics

Session:
  /help                           Show this help
  /clear                          Clear the screen
  /version                        Show version
  /info                           Show developer/build information
  /exit                           Exit interactive mode

Examples:
  /status
  /list --installed
  /list --security --json
  /search KB5034441
  /install --all --dry-run
  /pause --days 14
  /settings set active-hours 9-18
  /schedule set --day Friday --time 03:00 --auto-install
```

## Normal Help Should Still Work

These must continue to work outside interactive mode:

```bash
wum --help
wum list --help
wum settings --help
```

Do not replace the normal `System.CommandLine` help system.

## Testing Requirements

Add or update tests in `tests/WUM.CLI.Tests` where practical.

At minimum, test:

1. `wum` with no args starts interactive mode instead of returning command error.
2. `/list` maps to the same command path as `wum list`.
3. `/list --installed` preserves options.
4. `/settings set active-hours 9-18` preserves subcommands and arguments.
5. `/help`, `exit`, `/clear`, `/version`, and `/info` are handled.
6. Unknown slash commands show a helpful error and suggestion.
7. Existing one-shot commands still parse as before.

If fully automated REPL tests are difficult, extract the command conversion/tokenization logic into testable methods.

## Acceptance Criteria

The implementation is complete when all of these work:

```bash
wum
```

opens:

```text
WUM interactive mode
wum>
```

Inside interactive mode:

```text
/status
/list
/list --installed
/search KB5034441
/install --all --dry-run
/settings show
/settings set active-hours 9-18
/schedule show
/diagnose
/help
/version
/info
/clear
exit
```

And outside interactive mode, these still work:

```bash
wum status
wum list --installed
wum search KB5034441
wum install --all --dry-run
wum settings show
wum diagnose
wum --help
wum --version
wum --info
```

## Final Instruction

Implement interactive mode carefully and keep the project maintainable.

Do not rewrite the whole CLI.
Do not duplicate the existing command handlers.
Do not change the Windows Update business logic.

Only add a polished interactive command shell on top of the existing `System.CommandLine` command system.
