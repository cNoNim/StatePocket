+++
uri = "docs/tools/set_value"
name = "tool-set-value"
title = "Set Value"
description = "Extended usage notes for the set_value tool."
mimeType = "text/markdown"
toolName = "set_value"
related = [
  "docs/concepts/revisions",
  "docs/workflows/compare-and-set",
]
+++
# Set Value

`set_value` stores a JSON value under a key in a namespace.

Use it when you want to create or replace the current value for a key, optionally with:

- compare-and-set semantics through `expectedRevision`
- create-if-absent semantics through `ifAbsent`
- time-to-live through `ttlSeconds`

Typical uses:

- save structured task state
- update cached JSON blobs
- create durable preferences and small indexes

When storing plain text, pass `format: "text"` so the raw input is treated as a JSON string.
