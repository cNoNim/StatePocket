+++
uri = "docs/concepts/json-pointer"
name = "concept-json-pointer"
title = "JSON Pointer"
description = "How JSON Pointer paths are used for projections and patch targets."
mimeType = "text/markdown"
tags = ["concepts"]
related = [
  "docs/tools/get_value",
  "docs/tools/get_values",
  "docs/concepts/json-patch",
]
+++
# JSON Pointer

StatePocket uses JSON Pointer, defined by RFC 6901, for path-style access into stored JSON values.

StatePocket usage:

- `get_value.path`
- `get_values.path`
- patch targets inside `patch_value.patch`

Syntax notes:

- the root value is addressed by the empty pointer
- each path segment starts with `/`
- object property names are written as path segments
- array items are addressed by zero-based indexes
- `/` inside a property name is escaped as `~1`
- `~` inside a property name is escaped as `~0`

Examples:

Given this JSON:

```json
{
  "profile": {
    "name": "Oleg",
    "roles": ["admin", "editor"]
  },
  "items": [
    { "id": 1, "name": "first" },
    { "id": 2, "name": "second" }
  ],
  "a/b": 10,
  "m~n": 20
}
```

Useful pointers:

- `/profile/name`
- `/profile/roles/0`
- `/items/0`
- `/items/1/name`
- `/a~1b`
- `/m~0n`

Common mistakes:

- forgetting the leading `/`
- mixing JSON Pointer with JSONPath syntax like `$.profile.name`
- forgetting to escape `/` and `~` inside property names
- assuming it performs a search; JSON Pointer addresses one exact location

References:

- [RFC 6901](https://www.rfc-editor.org/rfc/rfc6901)
