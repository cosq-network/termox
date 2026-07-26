using System;
using System.IO;
using Termox.Services;
using Termox.ViewModels;
using Xunit;

namespace Termox.Tests;

public class SecurityAndPathTests
{
    [Fact]
    public void FingerprintComparisonAcceptsSha256PrefixAndWhitespace()
    {
        Assert.True(SshSecurity.FingerprintsMatch("SHA256:abc 123", "abc123"));
    }

    [Fact]
    public void FingerprintComparisonRejectsDifferentKeys()
    {
        Assert.False(SshSecurity.FingerprintsMatch("SHA256:expected", "SHA256:actual"));
    }

    [Fact]
    public void EmptyExpectedFingerprintMeansTrustOnFirstUse()
    {
        Assert.True(SshSecurity.FingerprintsMatch("", "SHA256:first-seen"));
    }

    [Fact]
    public void LocalPathSafetyAllowsChildrenOfRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "termox-test-root");
        var candidate = LocalPathSafety.EnsureWithinRoot(root, Path.Combine(root, "folder", "file.txt"));

        Assert.StartsWith(Path.GetFullPath(root), candidate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalPathSafetyRejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "termox-test-root");

        Assert.Throws<InvalidOperationException>(() =>
            LocalPathSafety.EnsureWithinRoot(root, Path.Combine(root, "..", "outside.txt")));
    }

    [Fact]
    public void LocalPathSafetyRejectsSiblingWithSamePrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "termox-root");

        Assert.Throws<InvalidOperationException>(() =>
            LocalPathSafety.EnsureWithinRoot(root, root + "-escape" + Path.DirectorySeparatorChar + "file.txt"));
    }

    [Fact]
    public void LegacyPlaintextCredentialsAreNotLoaded()
    {
        Assert.Equal(string.Empty, CredentialManager.DecryptCredential("legacy-password", "profile-id"));
    }

    [Fact]
    public void RelayCommandRaisesCanExecuteChanged()
    {
        var command = new RelayCommand(() => { });
        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }
}
