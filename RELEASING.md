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
A patch to the client alone is a GitHub release too, with the text and nothing attached, and is
never marked the latest, so that the latest release always carries the plugin.
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
4. **GitHub**, under Settings then Rules then Rulesets: `release tags`, for tags matching `v*`,
   blocking their creation, update and deletion, with repository administrators allowed past it.
   The `pypi` environment decides which runs may publish; this decides who may make the tag that
   starts one, and keeps a published tag from ever moving. A release does not fail without it,
   which is what makes it the one to forget.
5. **GitHub**, under the same: `release gate`, for tags matching `v*`, requiring the status check
   `gate passed` from GitHub Actions, with nobody allowed past it, administrators included. A
   `v` tag is then refused before it exists on a commit the gate has not passed on: an earlier
   commit of a merged pull request, a commit pushed past the ruleset on `master`, and the merge
   itself while its gate is still running. Its own ruleset, because `release tags` lets
   administrators past everything it holds. The source is named, since a status anybody with
   write access can post would satisfy the name alone; a workflow given `checks: write` can
   still make a check run of GitHub Actions under it, so this holds to write access, as the
   ruleset on `master` does. The release asks the same question again, so it still holds
   without this, and fails rather than publishing.
6. **No API token on PyPI**, for the account or the project. Publishing needs none, and one made
   later would be a second way to publish that passes by the tag, the checks before it and the
   reviewer on `pypi`.
7. **GitHub**, under Settings then Advanced Security: **Dependabot alerts** and **Dependabot
   security updates**. `dependabot.yml` moves each pin a week after its release, grouped by kind,
   and a security update is what arrives sooner: a pull request of its own as soon as an advisory
   names a version pinned here, with no cooldown, which GitHub opens only with both settings on.
   A release does not fail without them either. An advisory against a library the zip ships or an
   action the release runs then raises nothing, and its fix waits for the weekly update.

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

1. **A pull request that makes the version**: `Version` in `Directory.Build.props`, which the
   build writes into the routing document as well, and the version's heading in `CHANGELOG.md`,
   with the date in place of unreleased, as `## 0.1.0, 2026-10-25`, or
   `## 0.1.1, the plugin, 2026-11-02` for a patch. The gate checks it like any other, and the
   ruleset on `master` waits for it.

   **The same pull request names every dependency the version ships that moved**, since Dependabot
   updates them and cannot write the changelog. A `PackageReference` of the plugin ships, unless it
   is `PrivateAssets="all"`, and so does every package such a reference brings in, which no diff of
   the project shows: `SharpYaml` arrives with `Microsoft.OpenApi.Readers`. So the plugin's half is
   what restore resolves, read in a checkout of the previous release's tag and in a checkout of
   `origin/master`:

   ```powershell
   dotnet list src/KeePassGate package --include-transitive
   ```

   The client's half is a minimum in its `dependencies`, which is what this prints, against the
   previous release's tag:

   ```powershell
   git diff v0.1.0..origin/master -- pyproject.toml
   ```

   Nothing else ships: `uv.lock`, the development tools and the test projects' packages reach nobody
   who installs either half, and neither do the reference assemblies the build compiles against,
   `Microsoft.NETFramework.ReferenceAssemblies` and what it brings. Each that moved is an item of
   its own under **Dependencies**, even when it is the only dependency that moved, in alphabetical
   order, with packages that only ever move together sharing an item. It names the version it moved
   from and the version it moved to, because an advisory names a range and only the first tells
   whether the version being replaced was in it:

   ```markdown
   - **Dependencies**:
     - Microsoft.OpenApi and Microsoft.OpenApi.Readers from 1.6.30 to 1.6.31
     - Newtonsoft.Json from 13.0.3 to 13.0.4
     - SharpYaml from 2.1.1 to 2.1.5
   ```

2. **Merge it, wait for the gate on `master` to pass on the merge, then tag the merge and push
   the tag**. `release gate` refuses the push until then, which pushes nothing, and the same
   push is made again once it has passed:

   ```powershell
   git fetch origin
   git tag -a v0.1.0 -m "KeePassGate 0.1.0" origin/master
   git push origin v0.1.0
   ```

3. **Watch the run.** It refuses a patch whose heading names another half than the half it
   changed, and a commit the gate did not pass on, which `release gate` has already refused. A
   tag the workflow refuses has published nothing: delete it, locally and with
   `git push origin :refs/tags/v0.1.0`, which only an administrator can, past the tag ruleset,
   and tag the right commit. A tag that published is
   never moved, and a release found wrong is followed by the next patch.
