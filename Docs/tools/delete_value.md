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

Good fits:

- clear a cached entry
- remove stale task state
- remove a preference document entirely
