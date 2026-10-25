# Releasing

How a version of KeePassGate is published, for whoever maintains the repository. What a version
number means, and which half a release publishes, is in [`CONVENTIONS.md`](CONVENTIONS.md#versions);
this is how one gets out. [`.github/workflows/release.yml`](.github/workflows/release.yml) does
all of it, and its header has the reasons.

**A release is a `v` tag on a commit of `master`.** The version is the one `Directory.Build.props`
holds, and the tag only picks the commit to release: a tag naming another version, on a commit
`master` does not hold, or for a version the changelog still calls unreleased is refused before
anything is built. **The plugin is a GitHub release** under the tag, its zip and `SHA256SUMS`
attached and the changelog's section for the version as its text, and **the client is on PyPI**.
**PyPI comes last**, because a version number there can never be used again,
even after deleting it, so everything that could still fail fails before it.

## Once, before the first release

Each of these is a setting rather than a file, so none of it is in the repository, and a release
fails at the step that needs it until it is done.

1. **PyPI**: on the `pykeepassgate` project, under Publishing, a trusted publisher for GitHub with
   owner `mfrnd`, repository `KeePassGate`, workflow `release.yml` and environment `pypi`. That is
   what lets the workflow publish with no token kept anywhere.
2. **TestPyPI**, which has accounts and projects of its own: under the account's Publishing, a
   pending publisher for the project `pykeepassgate`, with the same owner, repository and workflow
   and the environment `testpypi`. The project is made by the first rehearsal's upload.
3. **GitHub**, under Settings then Environments: `pypi`, its deployments limited to tags matching
   `v*`, and `testpypi`, limited to the branch `master`. A reviewer on `pypi` is one more click
   before anything is published, and optional.
4. **The account token** that uploaded the placeholder `0.0.1` can then be revoked on PyPI, since
   nothing uses it.

## A rehearsal

**Run `release` by hand from the Actions page, on `master`.** It builds and checks everything a
release would, makes the GitHub release as a draft under a tag nobody pushed, which a draft does
not create, reads its files back and deletes it, and publishes the client to TestPyPI under a
development version of its own, `0.1.0.dev` and the run's number, since a number taken there
cannot be taken again either. What a
release still does for the first time is the address it publishes to and nothing else, so rehearse
after any change to the workflow and before the first release.

A pull request that touches what a release is built from runs the same workflow as a check, which
builds and checks everything and publishes nothing.

## A release

1. **A pull request that makes the version**: `Version` in `Directory.Build.props` and
   `info.version` in `src/KeePassGate/Spec/openapi.yaml`, which a test on each side holds together,
   and the version's heading in `CHANGELOG.md`, with the date in place of unreleased, as
   `## 0.1.0, 2026-10-26`, or `## 0.1.1, the plugin, 2026-11-02` for a patch. The gate checks it
   like any other, and the ruleset on `master` waits for it.
2. **Merge it, then tag the merge and push the tag**:

   ```powershell
   git fetch origin
   git tag -a v0.1.0 -m "KeePassGate 0.1.0" origin/master
   git push origin v0.1.0
   ```

3. **Watch the run.** A tag the workflow refuses has published nothing: delete it, locally and with
   `git push origin :refs/tags/v0.1.0`, and tag the right commit. A tag that published is never
   moved, and a release found wrong is followed by the next patch.
