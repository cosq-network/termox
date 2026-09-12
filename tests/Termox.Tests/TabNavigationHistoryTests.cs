using System.Windows.Input;
using Termox.Services;
using Termox.ViewModels;
using Xunit;

namespace Termox.Tests;

public class TabNavigationHistoryTests
{
    private class FakeTab : ITabViewModel
    {
        public FakeTab(string title) => Title = title;
        public string Title { get; }
        public ICommand CloseTabCommand { get; } = new RelayCommand(() => { });
        public ICommand DisconnectCommand { get; } = new RelayCommand(() => { });
    }

    [Fact]
    public void StartsWithNoBackOrForward()
    {
        var history = new TabNavigationHistory();

        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void PushThenBackReturnsPreviousTab()
    {
        var history = new TabNavigationHistory();
        var a = new FakeTab("A");
        var b = new FakeTab("B");

        history.Push(a);
        history.Push(b);

        Assert.True(history.CanGoBack);
        Assert.Same(a, history.GoBack());
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void BackThenForwardReturnsToWhereItWas()
    {
        var history = new TabNavigationHistory();
        var a = new FakeTab("A");
        var b = new FakeTab("B");
        history.Push(a);
        history.Push(b);

        history.GoBack();

        Assert.True(history.CanGoForward);
        Assert.Same(b, history.GoForward());
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void PushingAfterGoingBackDiscardsForwardBranch()
    {
        var history = new TabNavigationHistory();
        var a = new FakeTab("A");
        var b = new FakeTab("B");
        var c = new FakeTab("C");
        history.Push(a);
        history.Push(b);
        history.GoBack();

        history.Push(c);

        Assert.False(history.CanGoForward);
        Assert.Same(a, history.GoBack());
    }

    [Fact]
    public void GoBackAtStartReturnsNull()
    {
        var history = new TabNavigationHistory();
        history.Push(new FakeTab("A"));

        Assert.Null(history.GoBack());
    }

    [Fact]
    public void GoForwardAtEndReturnsNull()
    {
        var history = new TabNavigationHistory();
        history.Push(new FakeTab("A"));

        Assert.Null(history.GoForward());
    }

    [Fact]
    public void PushingSameTabAgainIsNoOp()
    {
        var history = new TabNavigationHistory();
        var a = new FakeTab("A");
        var b = new FakeTab("B");
        history.Push(a);
        history.Push(b);
        history.GoBack();

        history.Push(a);

        Assert.True(history.CanGoForward);
        Assert.Same(b, history.GoForward());
    }

    [Fact]
    public void RemovingClosedTabDropsItFromHistory()
    {
        var history = new TabNavigationHistory();
        var a = new FakeTab("A");
        var b = new FakeTab("B");
        var c = new FakeTab("C");
        history.Push(a);
        history.Push(b);
        history.Push(c);

        history.Remove(b);

        Assert.Same(a, history.GoBack());
    }

    [Fact]
    public void RemovingCurrentTabClampsIndex()
    {
        var history = new TabNavigationHistory();
        var a = new FakeTab("A");
        var b = new FakeTab("B");
        history.Push(a);
        history.Push(b);

        history.Remove(b);

        // Only "A" remains and it's now the current entry — nothing before or after it.
        Assert.False(history.CanGoForward);
        Assert.False(history.CanGoBack);
    }
}
