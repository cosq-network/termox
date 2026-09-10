# Termox CI/CD Integration Guide

This guide covers repository setup, GitHub Actions permissions, Apple signing
secrets, versioning, release execution, artifacts, validation, and
troubleshooting for Termox.

## 1. Workflows

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| .github/workflows/ci.yml | Pushes to `main`, `dev`, `release/**`, `hotfix/**` and pull requests targeting `main` or `dev` | Restores dependencies, runs tests, builds the application, and uploads coverage when available. |
| .github/workflows/release.yml | Automatic on push to `main` | Calculates a semantic version from Conventional Commits, validates, packages Windows/Linux/macOS, signs and notarizes macOS when secrets are present, tags, and publishes a GitHub Release. |

Release outputs are self-contained; end users do not need a separate .NET
installation.

## 2. Configure the GitHub repository

### Actions permissions

Open Settings → Actions → General:

1. Allow actions and reusable workflows from this repository and the required
   marketplace actions.
2. Set workflow permissions to Read and write permissions if repository policy
   permits it.
3. Keep pull-request write permissions disabled unless explicitly needed.

The workflows default to read-only contents access. Only the `publish` job in
the release workflow declares:

```yaml
permissions:
  contents: write
```

This permits only the final release job to push the release tag and publish
the GitHub Release. CI and all packaging jobs use read-only contents access.
The release workflow triggers only on pushes to `main`, so releases are gated
to the production branch.

### Branch protection

Protect the default branch and require:

- Pull requests before merging.
- The CI / Build and test status check.
- Up-to-date branches.
- At least one approving review.
- No force pushes or branch deletion.
- Release workflow access limited to trusted maintainers.

The CI workflow follows a GitFlow branch model and runs on `main`, `dev`,
`release/**`, and `hotfix/**`, plus pull requests targeting `main` or `dev`.
Update the workflow if the repository uses a different branch model.

## 3. Required GitHub secrets

Windows and Linux builds require no secrets. For a signed and notarized macOS
build, all six repository secrets below must be configured. When the signing
secrets are absent the macOS job still succeeds but produces an unsigned,
un-notarized bundle. The `APPLE_CERTIFICATE_P12_BASE64` value is decoded and
imported into a temporary keychain on the runner so that `codesign` can locate
`APPLE_SIGNING_IDENTITY`.

| Secret | Value |
| --- | --- |
| APPLE_CERTIFICATE_P12_BASE64 | Base64-encoded password-protected .p12 containing the Developer ID Application certificate and private key. |
| APPLE_CERTIFICATE_PASSWORD | Password used to export the .p12. |
| APPLE_SIGNING_IDENTITY | Exact identity, such as Developer ID Application: Example Company (TEAMID1234). |
| APPLE_ID | Apple Developer account email used for notarization. |
| APPLE_TEAM_ID | Apple Developer Team ID. |
| APPLE_APP_PASSWORD | Apple app-specific password. |

Create them at Settings → Secrets and variables → Actions → New repository
secret. Never commit or print certificates, private keys, passwords, or
app-specific passwords.

### Create the Apple certificate secret

The Apple Developer account needs a valid Developer ID Application certificate
with its private key.

On macOS:

1. Open Keychain Access.
2. Select the Developer ID Application certificate and its private key.
3. Export both as a password-protected .p12.
4. Use that export password as APPLE_CERTIFICATE_PASSWORD.
5. Verify the identity:

```bash
security find-identity -v -p codesigning
```

6. Copy the complete identity into APPLE_SIGNING_IDENTITY.
7. Encode the certificate:

```bash
base64 -i termox-developer-id.p12 | tr -d '\\n' > termox-developer-id.p12.base64
pbcopy < termox-developer-id.p12.base64
```

Paste the copied value into APPLE_CERTIFICATE_P12_BASE64. On Linux, use
base64 -w 0 termox-developer-id.p12 instead.

### Create the Apple app-specific password

Create an app-specific password through Apple ID account management. Use it as
APPLE_APP_PASSWORD, not the primary Apple ID password. The Apple ID must have
access to the Team ID associated with the certificate.

## 4. Versioning

Versioning is tag-driven. The baseline in Termox.csproj is 1.0.0. The workflow
finds the newest tag matching vX.Y.Z:

| Selection | Current | New |
| --- | --- | --- |
| patch | v1.2.3 | v1.2.4 |
| minor | v1.2.3 | v1.3.0 |
| major | v1.2.3 | v2.0.0 |

With no existing version tag, the workflow starts at 1.0.0, so the first patch
release is v1.0.1.

The calculated version is passed to .NET as:

```text
-p:Version=X.Y.Z -p:VersionPrefix=X.Y.Z
```

The About dialog reads the generated assembly version.

## 5. Publish a release

Before starting:

1. Merge the intended code into the default branch.
2. Confirm CI is green on the release commit.
3. Confirm all Apple secrets are present and valid, if signing/notarization is required.
4. Confirm the signing certificate has not expired.
5. Confirm no conflicting vX.Y.Z tag already exists.

Releases are automatic: when a push to `main` contains conventional commits,
the release workflow calculates the next version and packages all platforms.

Sequence:

```text
Calculate version → run tests → build Windows/Linux/macOS
→ download artifacts → verify downloaded artifacts → create and push vX.Y.Z
→ generate SHA256SUMS.txt → publish GitHub Release
```

The tag is created only after tests and all platform packaging jobs succeed.
Every release artifact upload and publish input is checked for expected files.
If a later publish step fails after the tag is pushed, the cleanup step removes
the incomplete release tag when possible.

## 6. Release artifacts

| Platform | Artifacts |
| --- | --- |
| Windows | Termox-X.Y.Z-windows-x64.exe (Inno Setup), Termox-X.Y.Z-windows-x64.msi (WiX), Termox-X.Y.Z-windows-x64.msix (MakeAppx). |
| Linux | Termox-X.Y.Z-linux-x64.tar.gz, Termox-X.Y.Z-linux-x64.deb (dpkg), Termox-X.Y.Z-linux-x64.rpm (rpmbuild). The packaging script maps linux-x64 to amd64, linux-arm64 to arm64, and linux-arm to armhf. |
| macOS | Termox-X.Y.Z-osx-arm64.dmg and .zip (Apple Silicon only). |

macOS DMGs are signed, notarized, and stapled when Apple secrets are configured.
ZIPs are created from the stapled app bundles. SHA256SUMS.txt contains checksums
for every Termox artifact:

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
# macOS:
shasum -a 256 -c SHA256SUMS.txt
```

## 7. Local validation

```bash
dotnet restore tests/Termox.Tests/Termox.Tests.csproj
dotnet test tests/Termox.Tests/Termox.Tests.csproj --configuration Release
dotnet build Termox.csproj --configuration Release
bash -n packaging/linux/build-linux.sh packaging/macos/build-macos.sh
ruby -e 'require "yaml"; YAML.load_file(".github/workflows/ci.yml"); YAML.load_file(".github/workflows/release.yml")'
```

A local macOS package requires a valid local signing identity and notarization
credentials:

```bash
export APPLE_SIGNING_IDENTITY='Developer ID Application: Example Company (TEAMID1234)'
export APPLE_ID='release@example.com'
export APPLE_TEAM_ID='TEAMID1234'
export APPLE_APP_PASSWORD='xxxx-xxxx-xxxx-xxxx'
bash packaging/macos/build-macos.sh 1.2.4 osx-arm64
```

The certificate must already be available in the local keychain for codesign.

## 8. Troubleshooting

### Missing macOS secret

When the signing secrets are absent the macOS job builds an unsigned bundle and
skips notarization rather than failing. For a distributable release, confirm all
six secrets are set with the correct spelling, repository scope, and Actions
access, and that the certificate has not expired.

### codesign cannot find the identity

Confirm the .p12 contains both the certificate and private key:

```bash
security find-identity -v -p codesigning
```

The result must match APPLE_SIGNING_IDENTITY exactly.

### Notarization fails

Check the Apple ID, app-specific password, Team ID, certificate validity, and
the stable bundle identifier com.cosqnetwork.termox. The notarytool --wait
output contains the submission status.

### A tag already exists

The workflow will not overwrite a tag. Inspect tags with:

```bash
git fetch --tags
git tag --list 'v[0-9]*' --sort=-version:refname | head
```

Choose the correct bump level; do not force-move a published release tag.

### Windows installer cannot find files

The installer expects publish output at artifacts/publish/win-x64 and the
executable Termox.exe.

### Linux .deb is unavailable

The Debian package is created when dpkg-deb is installed. The GitHub Ubuntu
runner includes it; the .tar.gz archive remains the portable fallback.

## 9. Security and maintenance checklist

- Rotate Apple app-specific passwords when maintainers change.
- Replace the .p12 secret before certificate expiration.
- Restrict release workflow execution to trusted maintainers.
- Keep third-party GitHub Actions pinned to reviewed commit SHAs.
- Keep contents: write limited to the release workflow.
- Never echo secret values or put them in artifact names.
- Review third-party actions and update major versions deliberately.
- Verify checksums after downloads.
- Keep published release tags immutable.
- Test the macOS arm64 build after native dependency changes.
