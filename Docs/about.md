+++
uri = "docs/about"
name = "about"
title = "About StatePocket"
description = "Overview of StatePocket and when to use it."
mimeType = "text/markdown"
related = [
  "docs/concepts/namespaces",
  "docs/concepts/revisions",
  "docs/concepts/json-pointer",
  "docs/concepts/json-path",
  "docs/concepts/json-patch",
  "docs/workflows/compare-and-set",
  "docs/workflows/partial-update",
]
+++
# About StatePocket

StatePocket is an MCP server for durable local JSON state backed by SQLite.

Use it when an agent or tool needs small persistent state that should survive across turns:

- checkpoints and task state
- structured preferences
- cached JSON fragments
- lightweight local memory

StatePocket is optimized for namespaced JSON values, JSON Pointer projections, JSONPath queries, and JSON Patch updates. It is not intended to replace a general-purpose database or a remote multi-user service.
