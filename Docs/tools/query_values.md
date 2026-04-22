+++
uri = "docs/tools/query_values"
name = "tool-query-values"
title = "Query Values"
description = "Extended usage notes for the query_values tool."
mimeType = "text/markdown"
toolName = "query_values"
related = [
  "docs/concepts/json-path",
  "docs/concepts/json-pointer",
]
+++
# Query Values

`query_values` scans values in a namespace, optionally filters them by key pattern, applies an optional JSONPath filter, and can compare a projected result with `equals`.

Use it when you do not already know the exact key set you need.

Good fits:

- find values by tag or status
- list records whose payload matches a condition
- scan keys with wildcard patterns and project one nested field

Path guide:

- `query` uses JSONPath and decides which stored values match
- `path` uses JSON Pointer and projects part of each matched value

Ordering and pagination:

- candidate keys are scanned in ascending lexicographic key order
- `cursor` is exclusive; pass the last matched key from the previous page to continue after it
- `nextCursor` is the last matched key returned in the current page when more results remain
- a follow-up page may contain zero matches even when `nextCursor` was present on the previous page, because matching happens after scanning candidate keys
- keep paging until `nextCursor` becomes `null`; an empty `values` object by itself does not guarantee the scan is finished
