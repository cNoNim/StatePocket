+++
uri = "docs/workflows/partial-update"
name = "workflow-partial-update"
title = "Partial Update"
description = "Updating one part of a JSON document without replacing the whole value."
mimeType = "text/markdown"
tags = ["workflows"]
requires_tools = ["patch_value"]
related = [
  "docs/tools/patch_value",
  "docs/concepts/json-patch",
]
+++
# Partial Update

Use `patch_value` when you want to change only part of a stored JSON document.

This is usually better than read-modify-write in the client when:

- the patch is small and local
- you already know the target JSON Pointer path
- you want to use `test` operations inside one patch document
