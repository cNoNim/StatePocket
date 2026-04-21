# StatePocket

`StatePocket` is an MCP server that provides persistent local JSON state for agents and tools, backed by SQLite.

It is designed for agents and skills that need durable local state close at hand: save structured JSON, read it back later, query it, patch it, and keep it in a single file instead of scattering state across prompts or temporary files.

## Install

Install `StatePocket` as a global .NET tool:

```bash
dotnet tool install --global StatePocket
```

The installed command is:

```text
statepocket
```

## Update

Update an existing global installation:

```bash
dotnet tool update --global StatePocket
```

## What It Does

- Stores JSON values under string keys
- Organizes data into namespaces
- Supports optional TTL-based expiration
- Supports conditional writes with `expectedRevision` and `ifAbsent`
- Reads whole values or projected fragments via JSON Pointer
- Queries stored values with JSONPath
- Applies JSON Patch documents
- Returns structured machine-readable errors for mutating tools
- Persists data in a local SQLite database

## When It Fits

`StatePocket` is a good fit when you want:

- local durable state for an MCP-enabled assistant
- a simple memory layer without running a separate database server
- JSON-native values instead of string-only key-value storage
- a store that can be inspected, backed up, and moved as a single SQLite file

It is not trying to be a general database, a search engine, or a multi-user remote service.

## Configuration

The `mcp` subcommand runs StatePocket as an MCP server over stdio.

```text
statepocket mcp --db-path /path/to/statepocket.db
```

MCP server options:

- `--db-path`
- `--enable-tools`
- `--disable-tools`

Environment variables:

- `STATEPOCKET_MCP_DB_PATH`
- `STATEPOCKET_MCP_ENABLE_TOOLS`
- `STATEPOCKET_MCP_DISABLE_TOOLS`

Tool filters accept comma-separated tool names.

## Tools

| Tool | Purpose |
| --- | --- |
| `set_value` | Stores a JSON value under a key, optionally with TTL. |
| `get_value` | Reads one key and can project a fragment with JSON Pointer. |
| `get_values` | Reads multiple keys in one call. |
| `query_values` | Finds stored values by key pattern and optional JSONPath filter, with optional equality matching. |
| `list_namespaces` | Lists namespaces that currently contain at least one live, unexpired key. |
| `list_keys` | Lists keys in a namespace, optionally by wildcard pattern. |
| `delete_value` | Deletes a key from a namespace. |
| `patch_value` | Applies a JSON Patch document to an existing stored value. |

## Standards

`StatePocket` uses a few standard JSON formats and query syntaxes:

- JSON Pointer for `path` arguments in read/query tools: RFC 6901
- JSON Patch for `patch_value`: RFC 6902
- JSONPath for `query_values`: RFC 9535

## Important Behavior

- `set_value.value` and `query_values.equals` are strings interpreted by `format`: use `json` for JSON text and `text` for raw strings.
- `patch_value.patch` is an RFC 6902 JSON Patch document encoded as JSON text.
- Values must be valid JSON after input parsing.
- Namespaces default to `default`.
- Revisions are monotonic within a namespace, not per key. Use them with `expectedRevision` for compare-and-set writes.
- `list_namespaces` only returns namespaces that currently contain at least one live, unexpired key.
- Pagination resumes after the last returned key or namespace from `nextCursor`.
- Mutating tools return structured MCP errors with machine-readable kinds.
- Expired values are ignored by reads and best-effort cleanup runs on startup.
- Data is stored in SQLite, so backing up or moving the state pocket is just copying one database file.

## License

MIT. See [LICENSE](LICENSE).
