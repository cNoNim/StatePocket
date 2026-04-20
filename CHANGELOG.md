# Changelog

All notable changes to this project will be documented in this file.

## [0.1.2] - 2026-04-20

- `patch_value` now returns the patched value.
- `set_value` and `patch_value` now return `expiresAt` when a TTL is present.
- Stored values and MCP tool results now include `revision`, and `set_value` supports conditional writes via `expectedRevision` and `ifAbsent`.
- MCP result payloads are now consistently camelCase, `patch_value` uses the new response shape, and tool results use typed contracts with a shared source-generated JSON policy.
- `query_values` now exposes a stricter schema: `equals` requires `query`.
- Internal SQLite storage code was reorganized into read, write, and infrastructure partials, with SQL kept close to the methods that use it.

### Compatibility

- This release includes MCP contract changes for response field names and `patch_value` output.
