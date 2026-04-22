+++
uri = "docs/workflows/compare-and-set"
name = "workflow-compare-and-set"
title = "Compare and Set"
description = "A safe revision-based update workflow."
mimeType = "text/markdown"
tags = ["workflows"]
requires_tools = ["set_value"]
related = [
  "docs/concepts/revisions",
  "docs/tools/set_value",
]
+++
# Compare and Set

Use this workflow when multiple writers may update the same namespace and you do not want silent lost updates.

Pattern:

1. Read the current value and revision.
2. Compute the next value locally.
3. Call `set_value` with `expectedRevision`.
4. If the write conflicts, re-read and retry with fresh data.

`revision_conflict` is marked as retryable because the workflow can usually succeed after refreshing state first. It does not mean blindly resending the same stale request.
