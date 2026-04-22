+++
uri = "docs/tools/list_namespaces"
name = "tool-list-namespaces"
title = "List Namespaces"
description = "Extended usage notes for the list_namespaces tool."
mimeType = "text/markdown"
toolName = "list_namespaces"
related = [
  "docs/concepts/namespaces",
  "docs/tools/list_keys",
]
+++
# List Namespaces

`list_namespaces` returns namespaces that currently contain at least one live, unexpired key.

Use it when you need to discover which logical state buckets currently exist before drilling into a specific namespace.
