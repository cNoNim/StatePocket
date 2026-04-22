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
