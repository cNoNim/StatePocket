# Contributing

## Local Checks

Before opening a PR or pushing a release tag, run:

```bash
dotnet tool restore
dotnet test StatePocket.slnx
jb cleanupcode StatePocket.slnx
jb inspectcode StatePocket.slnx --output=tmp/inspectcode.sarif
```

Expected result: `jb cleanupcode` leaves no diff, and `tmp/inspectcode.sarif` contains no findings (`results: []`).

## Versioning

`StatePocket` uses Semantic Versioning.

Until `1.0.0`, the normal release flow does not use prerelease versions.

The release version is updated manually in `VersionPrefix` in `Directory.Build.props`.

## Releases

Current release flow:

1. Update `VersionPrefix`.
2. Commit the version change.
3. Push a tag in the form `vX.Y.Z`.
4. Let the `Publish` GitHub Actions workflow publish the package to `nuget.org`.

Stable releases are published only from tags.

`workflow_dispatch` is not part of the normal stable release flow. It remains available only for exceptional manual publishes such as prerelease packages.
