+++
uri = "docs/concepts/namespaces"
name = "concept-namespaces"
title = "Namespaces"
description = "How StatePocket groups keys into namespaces."
mimeType = "text/markdown"
tags = ["concepts"]
related = [
  "docs/about",
  "docs/concepts/revisions",
]
+++
# Namespaces

StatePocket stores values under a `namespace` plus `key` pair.

Use namespaces to separate unrelated state, for example:

- `default` for general local state
- `tasks` for task checkpoints
- `profiles` for durable user preferences

Most tools default to the `default` namespace when none is provided.
