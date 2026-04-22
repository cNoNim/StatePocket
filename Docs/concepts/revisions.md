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

That means every successful write in the same namespace advances the revision counter.

Two related revision rules matter in practice:

- the `revision` returned from reads and writes is a namespace-scoped, monotonically increasing version number
- `expectedRevision` is checked against the current revision of the target key, not against a separate namespace-wide CAS token

Typical pattern:

1. Read the current value and revision.
2. Compute the next value.
3. Write with `expectedRevision` set to the revision you observed for that same key.

If another write wins first for that key, the operation fails with a revision conflict instead of silently overwriting data.

On a `revision_conflict` error, `expectedRevision` is the revision you supplied and `currentRevision` is the current revision of that key when one exists.
