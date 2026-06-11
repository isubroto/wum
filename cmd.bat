@echo off
echo Creating folder structure inside wum...

:: Create directories
mkdir "src\WUM.CLI\Commands"
mkdir "src\WUM.CLI\Helpers"
mkdir "src\WUM.Core\Models"
mkdir "src\WUM.Core\Services"
mkdir "src\WUM.Core\Helpers"
mkdir "tests\WUM.CLI.Tests"

:: Create Program.cs
type null > "src\WUM.CLI\Program.cs"

:: Create Command files
type null > "src\WUM.CLI\Commands\ListCommand.cs"
type null > "src\WUM.CLI\Commands\InstallCommand.cs"
type null > "src\WUM.CLI\Commands\UninstallCommand.cs"
type null > "src\WUM.CLI\Commands\SearchCommand.cs"
type null > "src\WUM.CLI\Commands\HideCommand.cs"
type null > "src\WUM.CLI\Commands\HistoryCommand.cs"
type null > "src\WUM.CLI\Commands\PauseCommand.cs"
type null > "src\WUM.CLI\Commands\StatusCommand.cs"
type null > "src\WUM.CLI\Commands\ScheduleCommand.cs"
type null > "src\WUM.CLI\Commands\SettingsCommand.cs"

:: Create Helper files
type null > "src\WUM.CLI\Helpers\ConsoleRenderer.cs"
type null > "src\WUM.CLI\Helpers\TableRenderer.cs"
type null > "src\WUM.CLI\Helpers\ProgressRenderer.cs"

echo Project structure created successfully!
pause