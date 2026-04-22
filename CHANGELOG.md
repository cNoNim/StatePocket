# Changelog

All notable changes to this project will be documented in this file.

## [0.1.3]

- StatePocket MCP servers now publish embedded Markdown documentation resources under `statepocket://docs/*`, including tool, concept, workflow, and about pages that agents can read directly from the server.
- Added the `statepocket://info` runtime resource with the active SQLite backend details, database path, and working directory, and server instructions now point clients at both the about page and runtime info.
- Added the `statepocket resource` CLI command to list and print embedded documentation resources from a local installation.
- `delete_value` is now idempotent: deleting a missing key succeeds with `deleted = false`, and successful deletes return `deletedValue`.
- JSON Pointer validation failures now use the dedicated `invalid_pointer` error kind instead of being reported as generic invalid JSON.
- `invalid_patch` payloads now include per-operation metadata such as `operationIndex`, `operation`, and `targetPath` when a parsed patch fails during application.
- Documentation was expanded around revision semantics, wildcard filters, and lexicographic pagination so MCP clients can reason about compare-and-set behavior and cursor traversal more reliably.

## [0.1.2]

- `set_value` and `query_values` now use explicit string inputs with `format: "json" | "text"` so MCP clients can pass JSON text or raw strings without relying on permissive `any` schemas.
- `patch_value` now accepts the RFC 6902 document as JSON text, returns the patched value, and includes `expiresAt` when a TTL is present.
- Stored values and MCP tool results now include `revision`, and `set_value` supports conditional writes via `expectedRevision` and `ifAbsent`.
- Revisions are monotonic within a namespace, and MCP descriptions now document that behavior directly in tool schemas.
- Mutation failures are now returned as structured MCP tool errors with machine-readable kinds such as `already_exists`, `revision_conflict`, `invalid_patch`, `invalid_json`, and `not_found`.
- MCP result payloads are now consistently camelCase, tool results use typed contracts with shared source-generated JSON contexts, and schema descriptions were tightened around pagination, live namespaces, and JSON/text examples.
- Internal SQLite storage code was reorganized into read, write, and infrastructure partials, with SQL kept close to the methods that use it.

### Compatibility

- This release includes MCP contract changes for request arguments, response field names, `patch_value` output, and mutation error payloads.
