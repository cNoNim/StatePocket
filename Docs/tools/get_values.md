+++
uri = "docs/tools/get_values"
name = "tool-get-values"
title = "Get Values"
description = "Extended usage notes for the get_values tool."
mimeType = "text/markdown"
toolName = "get_values"
related = [
  "docs/concepts/json-pointer",
  "docs/tools/get_value",
]
+++
# Get Values

`get_values` reads several keys in one call and can optionally apply the same JSON Pointer projection to each result.

Use it when you already know a small batch of keys and want one compact response instead of multiple `get_value` calls.
