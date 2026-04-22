+++
uri = "docs/tools/list_keys"
name = "tool-list-keys"
title = "List Keys"
description = "Extended usage notes for the list_keys tool."
mimeType = "text/markdown"
toolName = "list_keys"
related = [
  "docs/concepts/namespaces",
  "docs/tools/list_namespaces",
]
+++
# List Keys

`list_keys` lists keys in one namespace and can filter them with a wildcard pattern.

Use it when the key names themselves are the main thing you need, for example to browse a namespace or implement pagination.

Ordering and pagination:

- results are returned in ascending lexicographic key order
- `cursor` is exclusive; pass the last key from the previous page to continue after it
- `nextCursor` is the last key returned in the current page when more results remain

Pattern syntax:

- patterns use SQLite `GLOB` matching
- `*` matches any sequence of characters
- `?` matches exactly one character
- `[abc]` matches one character from a set or range like `[a-z]`
- there is no special `**` recursive form
