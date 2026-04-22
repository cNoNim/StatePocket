+++
uri = "docs/concepts/json-patch"
name = "concept-json-patch"
title = "JSON Patch"
description = "How RFC 6902 patch documents are applied by patch_value."
mimeType = "text/markdown"
tags = ["concepts"]
requires_tools = ["patch_value"]
related = [
  "docs/tools/patch_value",
  "docs/workflows/partial-update",
  "docs/concepts/json-pointer",
]
+++
# JSON Patch

StatePocket uses JSON Patch, defined by RFC 6902, for `patch_value`.

StatePocket usage:

- only in `patch_value.patch`
- patch operation paths are JSON Pointer paths
- the whole patch fails if any operation fails

A patch document is an array of operations such as:

- `add`
- `remove`
- `replace`
- `move`
- `copy`
- `test`

Each operation targets JSON Pointer paths inside the stored value.

Example:

Given this stored value:

```json
{
  "profile": {
    "name": "Oleg",
    "team": "core"
  },
  "tags": ["admin"]
}
```

You can apply:

```json
[
  { "op": "replace", "path": "/profile/team", "value": "platform" },
  { "op": "add", "path": "/tags/-", "value": "beta" }
]
```

Operation notes:

- `add`
  Inserts or appends a value.
- `remove`
  Removes a value at a target path.
- `replace`
  Overwrites an existing value.
- `move`
  Moves a value from one path to another.
- `copy`
  Copies a value from one path to another.
- `test`
  Verifies a value before continuing. If it fails, the whole patch fails.

When to use `patch_value` instead of `set_value`:

- use `patch_value` for local updates to part of a document
- use `set_value` when you already have the whole next value
- use `set_value` with `expectedRevision` when the main concern is compare-and-set semantics across the whole document

Common mistakes:

- using non-pointer syntax like `$.profile.name` inside `path`
- assuming `add` and `replace` behave the same on missing paths
- forgetting that `test` failure aborts the whole patch
- building a patch against stale document structure

References:

- [RFC 6902](https://www.rfc-editor.org/rfc/rfc6902)
