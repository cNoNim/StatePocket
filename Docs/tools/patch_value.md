+++
uri = "docs/tools/patch_value"
name = "tool-patch-value"
title = "Patch Value"
description = "Extended usage notes for the patch_value tool."
mimeType = "text/markdown"
toolName = "patch_value"
related = [
  "docs/concepts/json-patch",
  "docs/workflows/partial-update",
]
+++
# Patch Value

`patch_value` applies an RFC 6902 JSON Patch document to an existing stored value.

Use it when you want to change part of a document without replacing the whole value yourself.

Typical uses:

- update one nested field
- append to an array
- enforce a precondition with a `test` operation
