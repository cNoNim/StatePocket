+++
uri = "docs/concepts/revisions"
name = "concept-revisions"
title = "Revisions"
description = "Monotonic namespace-scoped revisions and compare-and-set behavior."
mimeType = "text/markdown"
tags = ["concepts"]
requires_tools = ["set_value"]
related = [
  "docs/tools/set_value",
  "docs/workflows/compare-and-set",
]
+++
# Revisions

StatePocket revisions are monotonic within a namespace, not per key.

That means every successful write in the same namespace advances the revision counter. Use revisions for compare-and-set style updates with `expectedRevision`.

Typical pattern:

1. Read the current value and revision.
2. Compute the next value.
3. Write with `expectedRevision` set to the revision you observed.

If another write wins first, the operation fails with a revision conflict instead of silently overwriting data.
