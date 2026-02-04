# Windows Service Utils

Wrap any executable, script, or command as a Windows Service—no code changes required.

## Features

- Run executables, batch scripts, PowerShell, Python, Node.js, or any CLI as a Windows Service
- Full lifecycle management: install, start, stop, restart, uninstall
- Environment variables, working directory, dependencies, logging, custom service accounts
- Automatic script interpreter detection (`.bat`, `.cmd`, `.ps1`, `.py`, `.js`)

## Quick Start

> [!IMPORTANT]
> Requires administrator privileges.

```cmd
wsu add MyApp --command "C:\Apps\myapp.exe" --start-type auto
wsu start MyApp
```

## Installation

**Requirements:** Windows 7+, .NET Framework 4.7.2+

Download `wsu.exe` from [Releases](https://github.com/truemogician/Windows-Service-Utils/releases) and place it in your PATH, or build from source:

```cmd
git clone https://github.com/truemogician/Windows-Service-Utils.git
cd Windows-Service-Utils
dotnet build -c Release
```

## Usage

### Add a Service

```cmd
wsu add <name> --command <command> [options]
```

**Examples:**

```cmd
# Basic
wsu add WebServer --command "C:\Server\web.exe" --start-type auto

# With environment and logging
wsu add ApiService --command "node server.js" --working-directory "C:\Apps" ^
  --env "NODE_ENV=production" --stdout-log "C:\Logs\api.log" --start-type auto

# With dependencies
wsu add MyApp --command "C:\Apps\app.exe" --depend "MSSQLSERVER" --start-type delayed-auto
```

### Options

| Option          | Description                                                    |
| --------------- | -------------------------------------------------------------- |
| `-c, --command` | Command line to execute (required)                             |
| `-d, --display` | Display name in services.msc                                   |
| `--description` | Service description                                            |
| `-w, --working` | Working directory                                              |
| `--start-type`  | `auto`, `manual`, `disabled`, `delayed-auto` (default: manual) |
| `--depend`      | Comma-separated service dependencies                           |
| `--username`    | Service account (default: LocalSystem)                         |
| `--password`    | Service account password                                       |
| `--env`         | Environment variable `KEY=VALUE` (repeatable)                  |
| `--stdout-log`  | Stdout log file path                                           |
| `--stderr-log`  | Stderr log file path                                           |

### Manage Services

```cmd
wsu start <name>
wsu stop <name>
wsu restart <name>
wsu remove <name>    # Stops, uninstalls, deletes config
```

### Script Interpreters

Auto-detects scripts by extension (`.bat`, `.cmd`, `.ps1`, `.py`, `.js`).

```cmd
wsu interpreter list                                    # Show mappings
wsu interpreter set .rb --exec "ruby.exe"               # Add custom
wsu interpreter remove .rb                               # Remove custom
```

## Configuration

Configurations are stored in `%ProgramData%\WindowsServiceUtils\configs\<name>.json`:

```json
{
  "name": "MyService",
  "commandLine": "C:\\Apps\\app.exe",
  "workingDirectory": "C:\\Apps",
  "startType": "auto",
  "environmentVariables": {
    "APP_ENV": "production"
  },
  "stdoutLogFile": "C:\\Logs\\app.log"
}
```

## Troubleshooting

**Service won't start:** Check Event Viewer (`eventvwr.msc` → Windows Logs → Application). Common issues: `wsu.exe` moved, wrong command path, missing dependencies, permission issues.

**Process doesn't stop cleanly:** Ensure your app handles `Ctrl+C` / `SIGINT` and completes shutdown within 30 seconds.

**Script doesn't run:** Verify interpreter with `wsu interpreter list`. Test manually first.

**Permission errors:** Services run as LocalSystem by default. Use `--username` for network access or specific permissions.

## Examples

```cmd
# Python Flask app
wsu add FlaskApp --command "C:\Apps\flask\app.py" ^
  --env "FLASK_ENV=production" --stdout-log "C:\Logs\flask.log" --start-type auto

# Node.js server
wsu add NodeAPI --command "node server.js" --working-directory "C:\Apps\api" ^
  --env "NODE_ENV=production" --env "PORT=3000" --start-type delayed-auto

# Service with SQL dependency
wsu add DataService --command "C:\Apps\app.exe" --depend "MSSQLSERVER" --start-type auto
```