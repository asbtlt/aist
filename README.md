# Aist

AI-assisted project management for software developers and AI agents.

Aist is a lightweight project management tool designed for the AI era. It bridges the gap between human project managers and AI developer agents through a simple CLI interface and REST API.

## Features

- **Project Management**: Create and manage projects
- **Job Tracking**: Track features, fixes, refactors, chores, formatting, and documentation tasks
- **User Stories**: Convert jobs into actionable user stories with acceptance criteria
- **Progress Logging**: Keep track of development progress
- **Task Viewer**: Browse auto-refreshing jobs, stories, criteria, and logs in a lightweight web UI
- **MCP Server**: Expose Aist tools directly to AI agents through Model Context Protocol
- **AI-Native**: Built for both human developers and AI agents
- **Native Performance**: Distributed as Native AOT binaries for fast startup and minimal dependencies

## Architecture

```
┌─────────────────┐
│   Aist CLI      │
└────────┬────────┘
         │
┌────────▼────────┐     ┌──────────────────┐     ┌─────────────┐
│   Aist MCP      │────▶│  Aist Backend    │────▶│  SQLite DB  │
│   Task Viewer   │     │ REST API + UI    │     │  (aist/)    │
└─────────────────┘     └──────────────────┘     └─────────────┘
```

- **Aist.Cli**: Command-line interface for interacting with the system
- **Aist.Mcp**: Model Context Protocol server for AI agent integrations
- **Aist.Backend**: ASP.NET Core Web API and static task viewer
- **Database**: SQLite for zero-configuration setup

## Installation

### Quick Install

#### macOS / Linux

```bash
curl -sSL https://raw.githubusercontent.com/rodd-oss/aist/main/install.sh | bash
```

Or with a specific version:
```bash
curl -sSL https://raw.githubusercontent.com/rodd-oss/aist/main/install.sh | bash -s v1.0.0
```

#### Windows

**PowerShell:**
```powershell
irm https://raw.githubusercontent.com/rodd-oss/aist/main/install.ps1 | iex
```

**Or download and run:**
```powershell
# Download the installer
Invoke-WebRequest -Uri https://raw.githubusercontent.com/rodd-oss/aist/main/install.ps1 -OutFile install.ps1

# Run it
.\install.ps1

# Or with a specific version
.\install.ps1 -Version v1.0.0
```

### Manual Installation

Download the appropriate binary for your platform from the [Releases](https://github.com/rodd-oss/aist/releases) page:

| Platform | Architecture | Download |
|----------|-------------|----------|
| Linux | x64 | `aist-linux-x64.tar.gz` |
| Linux | ARM64 | `aist-linux-arm64.tar.gz` |
| macOS | x64 | `aist-osx-x64.tar.gz` |
| macOS | ARM64 (Apple Silicon) | `aist-osx-arm64.tar.gz` |
| Windows | x64 | `aist-win-x64.zip` |
| Windows | ARM64 | `aist-win-arm64.zip` |

Extract and place the binary in a directory in your PATH.

## Usage

### Setup Backend

Before using the CLI, you need to run the backend server:

```bash
# Using Docker
docker compose up -d --build backend

# Or run locally
cd src/Aist.Backend
dotnet run
```

The backend API will be available at `http://localhost:5192/api/v1` by default.
The task viewer will be available at `http://localhost:5192/`.

### CLI Commands

```bash
# Show help
aist --help

# Project management
aist project list
aist project create --title "My New Project"
aist project delete --id <project-id>

# Job management
aist job list
aist job list --project-id <project-id>
aist job create --project-id <id> --type feature --title "Add login" --description "..." --slug add-login
aist job pull --job-id <id>
aist job done --job-id <id> --pr-title "..." --pr-description "..."

# User stories
aist story list --job-id <id>
aist story create --job-id <id> --title "..." --who "..." --what "..." --why "..." --priority 1
aist story complete --story-id <id>

# Acceptance criteria
aist criteria list --story-id <id>
aist criteria create --story-id <id> --description "..."
aist criteria check --criteria-id <id>
aist criteria uncheck --criteria-id <id>

# Progress logs
aist log list --story-id <id>
aist log add --story-id <id> --text "..."
```

### MCP Server

Run the MCP server when connecting Aist to an AI agent:

```bash
dotnet run --project src/Aist.Mcp/Aist.Mcp.csproj
```

The MCP server exposes tools for projects, jobs, user stories, acceptance criteria, progress logs, and backend health checks. It uses UTF-8 input and output so Cyrillic and other non-ASCII text is preserved.

Available tools:

- `health_check`
- `project_list`, `project_create`, `project_delete`
- `job_list`, `job_get`, `job_create`, `job_update`, `job_update_status`, `job_delete`
- `story_list_by_job`, `story_create`, `story_set_complete`
- `criteria_list_by_story`, `criteria_create`, `criteria_set_met`
- `log_list_by_story`, `log_add`

### Environment Variables

```bash
# Backend URL for CLI and MCP (default: http://localhost:5192/api/v1)
export AIST_API_URL=http://localhost:5192/api/v1

# Database path for backend (relative to backend working directory)
export ConnectionStrings__DefaultConnection="Data Source=aist/main.db"

# MCP transport mode (optional: jsonl for line-delimited JSON-RPC)
export AIST_MCP_TRANSPORT=jsonl
```

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Building

```bash
# Build entire solution
dotnet build Aist.slnx

# Build CLI only
dotnet build src/Aist.Cli/Aist.Cli.csproj

# Build Backend only
dotnet build src/Aist.Backend/Aist.Backend.csproj

# Build MCP server only
dotnet build src/Aist.Mcp/Aist.Mcp.csproj
```

### Running

```bash
# Run backend
cd src/Aist.Backend
dotnet run

# Run CLI
cd src/Aist.Cli
dotnet run -- <command>

# Run MCP server
cd src/Aist.Mcp
dotnet run
```

### Publishing Native AOT

```bash
# Publish for current platform
dotnet publish src/Aist.Cli/Aist.Cli.csproj \
  -c Release \
  -p:PublishAot=true \
  -p:PublishSingleFile=true \
  --self-contained \
  -o ./publish

# Publish for specific platform
dotnet publish src/Aist.Cli/Aist.Cli.csproj \
  -c Release \
  -r linux-x64 \
  -p:PublishAot=true \
  -p:PublishSingleFile=true \
  --self-contained \
  -o ./publish/linux-x64
```

### Running Tests

```bash
dotnet test
```

## Docker

```bash
# Build and run with Docker Compose
docker compose up -d --build backend

# View logs
docker compose logs -f backend

# Stop
docker compose down

# Rebuild without cache
docker compose build --no-cache backend
docker compose up -d backend
```

## Workflow

Aist is designed around a simple but powerful workflow:

### 1. Planning Phase
```bash
# Create a project
aist project create --title "Website Redesign"

# Create jobs with detailed descriptions
aist job create \
  --project-id <id> \
  --type feature \
  --title "Implement dark mode" \
  --description "Add dark mode toggle and theme support" \
  --slug dark-mode

# Break down into user stories
aist story create \
  --job-id <id> \
  --title "Theme toggle component" \
  --who "user" \
  --what "toggle between light and dark themes" \
  --why "reduce eye strain in low light" \
  --priority 1

# Add acceptance criteria
aist criteria create \
  --story-id <id> \
  --description "Toggle button visible in header"

aist criteria create \
  --story-id <id> \
  --description "Theme preference persists across sessions"
```

### 2. Development Phase
```bash
# Pull the job (creates git branch)
aist job pull --job-id <id>

# Work through stories, logging progress
aist log add --story-id <id> --text "Created ThemeContext provider"
aist criteria check --criteria-id <id>

# Mark story complete when all criteria met
aist story complete --story-id <id>
```

### 3. Completion Phase
```bash
# Create PR and mark job done
aist job done \
  --job-id <id> \
  --pr-title "feat: implement dark mode" \
  --pr-description "Implements theme toggle..."
```

## API

The backend exposes a versioned REST API. See `src/Aist.Backend/Controllers/` for endpoints.

Default API base URL: `http://localhost:5192/api/v1`
Health check: `http://localhost:5192/api/health`

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

[MIT](LICENSE)

## Support

For issues and feature requests, please use the [GitHub Issues](https://github.com/rodd-oss/aist/issues) page.
