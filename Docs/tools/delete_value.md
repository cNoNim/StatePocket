+++
uri = "docs/tools/delete_value"
name = "tool-delete-value"
title = "Delete Value"
description = "Extended usage notes for the delete_value tool."
mimeType = "text/markdown"
toolName = "delete_value"
+++
# Delete Value

`delete_value` removes one key from a namespace.

Use it when you want a hard delete instead of patching or overwriting the value.

The result is idempotent:

- `deleted = true` means a live key was removed
- `deleted = false` means there was nothing live to remove
- `deletedValue` is returned only when something was deleted
- if the stored JSON value was `null`, then `deletedValue` is present and equal to `null`

Good fits:

- clear a cached entry
- remove stale task state
- remove a preference document entirely
