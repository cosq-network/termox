# Release packaging

The platform scripts are invoked by `.github/workflows/release.yml` and accept
`VERSION` followed by a runtime identifier. They publish self-contained builds
so end users do not need to install .NET separately.

- Windows: Inno Setup installer from `packaging/windows/termox.iss`, WiX MSI from `packaging/windows/termox.wxs`, and an MSIX package built with Windows SDK MakeAppx from `packaging/windows/AppxManifest.xml`.
- Linux: `.deb` and `.rpm` artifacts, plus a `.tar.gz` archive, from `packaging/linux/build-linux.sh`.
- macOS: `.dmg` disk image installer from `packaging/macos/build-macos.sh`.

The macOS release workflow requires these GitHub Actions secrets and signs and
notarizes both architecture-specific DMGs before publishing them:

- `APPLE_CERTIFICATE_P12_BASE64`
- `APPLE_CERTIFICATE_PASSWORD`
- `APPLE_SIGNING_IDENTITY`
- `APPLE_ID`
- `APPLE_TEAM_ID`
- `APPLE_APP_PASSWORD`

For repository permissions, secret preparation, version increments, release
operation, checksums, and troubleshooting, see
[the CI/CD integration guide](../docs/CI-CD-INTEGRATION.md).
