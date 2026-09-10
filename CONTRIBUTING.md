# Contributing to Termox

Thank you for contributing to Termox. This document explains the branch model
and commit message convention the CI/CD pipeline depends on. Please read it
before opening a pull request.

---

## Branch model (GitFlow)

Termox uses a simplified GitFlow model with two permanent branches and three
types of short-lived branches.

```
main ──────────────────────────────────────────────────────► production
       ↑  merge via PR         ↑  merge via PR
  release/x.y.z           hotfix/x.y.z
       ↑  cut from dev          ↑  cut from main
dev  ──────────────────────────────────────────────────────► integration
       ↑  merge via PR
  feature/short-description
```

### Permanent branches

| Branch | Purpose |
|--------|---------|
| `main` | Production. Every commit here represents a shipped release. Direct pushes are not allowed — merge via PR only. Pushing to `main` automatically triggers the release workflow. |
| `dev` | Integration. All finished feature work lands here first. Must always be in a buildable, testable state. |

### Short-lived branches

| Branch pattern | Cut from | Merges into | Purpose |
|----------------|----------|-------------|---------|
| `feature/<description>` | `dev` | `dev` | New features or non-urgent improvements. |
| `release/<version>` | `dev` | `main` **and** `dev` | Release preparation: version bump in docs, last-minute fixes, changelog updates. No new features. After merging to `main` also merge back to `dev`. |
| `hotfix/<description>` | `main` | `main` **and** `dev` | Urgent production fixes. After merging to `main` also merge back to `dev`. |

### Rules

- Feature branches must be created from the latest `dev`, not from `main`.
- Release and hotfix branches must be merged into **both** `main` and `dev` to keep them in sync.
- Never merge `main` back into `dev` directly; use a hotfix or release branch.
- Delete short-lived branches after merging.

---

## Commit messages (Conventional Commits)

The release workflow reads your commit messages to decide the version bump
automatically. There is no manual version selection — the messages drive
everything.

Termox follows the [Conventional Commits 1.0.0](https://www.conventionalcommits.org/)
specification.

### Format

```
<type>[optional scope]: <short description>

[optional body]

[optional footer(s)]
```

The `<type>` must be one of the values listed below. The `<short description>`
must be written in the imperative, present tense ("add feature", not "added
feature") and must not end with a period. The total header line should stay
under 72 characters.

### Types and their effect on versioning

| Type | Version bump | Use for |
|------|-------------|---------|
| `feat` | **minor** | A new feature visible to end users |
| `fix` | patch | A bug fix visible to end users |
| `chore` | patch | Maintenance tasks, dependency updates |
| `docs` | patch | Documentation only changes |
| `style` | patch | Formatting, whitespace — no logic change |
| `refactor` | patch | Code restructure without feature or fix |
| `perf` | patch | Performance improvement |
| `test` | patch | Adding or updating tests |
| `build` | patch | Build system or external dependency changes |
| `ci` | patch | Changes to CI/CD configuration |

### Breaking changes → major bump

A commit is a breaking change when **either** of the following is true:

1. The type is followed by `!` before the colon:

   ```
   feat!: remove legacy connection profile format
   fix(sftp)!: drop support for SFTPv3
   ```

2. The commit body or footer contains the literal text `BREAKING CHANGE:`:

   ```
   feat: restructure sessions storage

   BREAKING CHANGE: sessions.json schema version 2 is not backwards compatible.
   Existing profiles must be re-imported.
   ```

### Examples

```
feat(sftp): add bulk rename with regex pattern

fix(terminal): correct ANSI reset sequence handling

chore(deps): upgrade SSH.NET to 2026.1.0

docs: update installation section in README

feat!: replace sessions.json with encrypted SQLite store

refactor(auth): extract host key verification into SshSecurity

ci: add linux-arm64 to release matrix
```

### What happens when you push to main

The release workflow scans all commits since the last `vX.Y.Z` tag:

- Any `BREAKING CHANGE` or `type!:` → **major** bump (`1.2.3 → 2.0.0`)
- Any `feat:` commit (and no breaking change) → **minor** bump (`1.2.3 → 1.3.0`)
- Only `fix:`/`chore:`/etc. → **patch** bump (`1.2.3 → 1.2.4`)
- No conventional commits at all → **no release created** (the workflow exits cleanly)

The highest-priority bump level wins. A single breaking change in an otherwise
all-patch set of commits still produces a major bump.

---

## Pull request checklist

Before opening a PR:

- [ ] Branch is created from the correct base (`dev` for features, `main` for hotfixes).
- [ ] All commits follow the Conventional Commits format above.
- [ ] `dotnet restore && dotnet test tests/Termox.Tests/Termox.Tests.csproj --configuration Release` passes locally.
- [ ] New behaviour is covered by tests where practical.
- [ ] No secrets, passwords, private keys, or personal data in any committed file.
- [ ] The PR title itself follows Conventional Commits format (used as the merge commit message on squash merges).

---

## Release procedure

Releases are fully automatic. You do not run anything manually.

```
feature/my-work  →  PR  →  dev
                              │
                         (when ready)
                              │
                   release/1.3.0  →  PR  →  main  ──► automatic release v1.3.0
                              │                   ↘
                              └─────────────────►  dev  (sync merge)
```

1. When `dev` accumulates enough work for a release, cut a `release/x.y.z`
   branch from `dev`.
2. Use the release branch for final documentation updates and minor fixes only.
   Commit messages on this branch must still follow Conventional Commits.
3. Open a PR from `release/x.y.z` to `main`. When it merges, the release
   workflow fires automatically, calculates the version from commits, builds
   all platform packages, and publishes the GitHub Release.
4. Merge the same `release/x.y.z` branch back into `dev` to keep branches in sync.

For urgent production fixes:

1. Cut a `hotfix/<description>` branch from `main`.
2. Fix the issue, using `fix:` or `fix!:` commit messages as appropriate.
3. PR into `main` → automatic release fires.
4. PR or direct merge into `dev` to keep branches in sync.

---

## Reporting bugs

Include: operating system, Termox version, connection type (SSH/SFTP),
steps to reproduce, expected behaviour, and actual behaviour.

Do **not** include passwords, private keys, host names, or other sensitive
connection information in bug reports.
