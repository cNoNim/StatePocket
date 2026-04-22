+++
uri = "docs/concepts/json-path"
name = "concept-json-path"
title = "JSONPath"
description = "How JSONPath filters are used by query_values."
mimeType = "text/markdown"
tags = ["concepts"]
requires_tools = ["query_values"]
related = [
  "docs/tools/query_values",
  "docs/concepts/json-pointer",
]
+++
# JSONPath

StatePocket uses JSONPath, defined by RFC 9535, for filtering JSON values in `query_values`.

StatePocket usage:

- `query_values.query`
- optionally combined with `query_values.equals`
- often paired with `pattern` to narrow the key scan

What it is:

JSONPath is a query language for selecting data from JSON documents. In StatePocket it is used for filtering values during scans, not for addressing one exact path in one document.

Typical examples:

- `$.status`
- `$.tags[*]`
- `$.profile.name`

Examples:

Given this stored value:

```json
{
  "status": "active",
  "profile": {
    "name": "Oleg",
    "team": "core"
  },
  "tags": ["admin", "beta"],
  "age": 26
}
```

Useful queries:

- `$.status`
  Returns the `status` field.
- `$.profile.name`
  Returns the nested `name`.
- `$.tags[*]`
  Returns all tag values.
- `$.age`
  Useful together with `equals: 26` and `format: "json"`.

How `equals` interacts with `query`:

- `query: "$.age"` with `equals: 26` and `format: "json"` matches numeric `26`
- `query: "$.tags[*]"` with `equals: "admin"` and `format: "text"` matches the string `"admin"`
- `equals` is applied to the values produced by the JSONPath query, not to the whole document

Common mistakes:

- using JSONPath where a JSON Pointer path is expected
- expecting JSONPath to identify exactly one location
- mixing JSON number matching with string matching by choosing the wrong `format`
- forgetting that `query_values` still scans candidate values; JSONPath filters results, it does not replace key selection

References:

- [RFC 9535](https://www.rfc-editor.org/rfc/rfc9535)
