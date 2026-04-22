+++
uri = "docs/tools/get_value"
name = "tool-get-value"
title = "Get Value"
description = "Extended usage notes for the get_value tool."
mimeType = "text/markdown"
toolName = "get_value"
related = [
  "docs/concepts/json-pointer",
  "docs/tools/get_values",
]
+++
# Get Value

`get_value` reads a single key and can optionally project a fragment with JSON Pointer.

Use it when you know the exact key you want and do not need to fetch multiple keys in one round trip.

Good fits:

- load one task checkpoint
- read one preference document
- project one nested field with `path`
