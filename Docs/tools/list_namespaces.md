+++
uri = "docs/tools/list_namespaces"
name = "tool-list-namespaces"
title = "List Namespaces"
description = "Extended usage notes for the list_namespaces tool."
mimeType = "text/markdown"
toolName = "list_namespaces"
related = [
  "docs/concepts/namespaces",
  "docs/tools/list_keys",
]
+++
# List Namespaces

`list_namespaces` returns namespaces that currently contain at least one live, unexpired key.

Use it when you need to discover which logical state buckets currently exist before drilling into a specific namespace.

Ordering and pagination:

- results are returned in ascending lexicographic namespace order
- `cursor` is exclusive; pass the last namespace from the previous page to continue after it
- `nextCursor` is the last namespace returned in the current page when more results remain

Pattern syntax:

- patterns use SQLite `GLOB` matching
- `*` matches any sequence of characters
- `?` matches exactly one character
- `[abc]` matches one character from a set or range like `[a-z]`
- there is no special `**` recursive form
