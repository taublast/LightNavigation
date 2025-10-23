using System.Text;

namespace LightNavigation.TestApp.Pages;

public partial class LifecycleAwarePage : ContentPage, INavigationAware
{
    private readonly int _pageNumber;
    private readonly StringBuilder _eventLog = new StringBuilder();
    private readonly Action<string>? _logCallback;
    private int _callCount = 0;

    public LifecycleAwarePage(int pageNumber, Action<string>? logCallback = null)
    {
        InitializeComponent();

        _pageNumber = pageNumber;
        _logCallback = logCallback;

        PageNumberLabel.Text = $"Page #{pageNumber}";

        LogEvent("Constructor", "Page constructed");
    }

    #region INavigationAware Implementation

    public void OnPushing()
    {
        _callCount++;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPushingLabel.Text = $"✓ Called at {timestamp}";
            OnPushingLabel.TextColor = Colors.Green;
            ((Border)OnPushingLabel.Parent).BackgroundColor = Color.FromArgb("#C8E6C9");
        });

        LogEvent("OnPushing", "Called just before page is pushed");
        _logCallback?.Invoke($"LifecycleAware #{_pageNumber}: OnPushing()");
    }

    public void OnTopmost()
    {
        _callCount++;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnTopmostLabel.Text = $"✓ Called at {timestamp}";
            OnTopmostLabel.TextColor = Colors.Green;
            ((Border)OnTopmostLabel.Parent).BackgroundColor = Color.FromArgb("#C8E6C9");
        });

        LogEvent("OnTopmost", "Called when page becomes visible!");
        _logCallback?.Invoke($"LifecycleAware #{_pageNumber}: OnTopmost() - Page is now visible!");
    }

    public void OnCovered()
    {
   
    }

    public void OnPopping()
    {
        _callCount++;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPoppingLabel.Text = $"✓ Called at {timestamp}";
            OnPoppingLabel.TextColor = Colors.Orange;
            ((Border)OnPoppingLabel.Parent).BackgroundColor = Color.FromArgb("#FFE0B2");
        });

        LogEvent("OnPopping", "Called just before page is popped");
        _logCallback?.Invoke($"LifecycleAware #{_pageNumber}: OnPopping()");
    }

    public void OnRemoved()
    {
        _callCount++;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnRemovedLabel.Text = $"✓ Called at {timestamp}";
            OnRemovedLabel.TextColor = Colors.Red;
            ((Border)OnRemovedLabel.Parent).BackgroundColor = Color.FromArgb("#FFCDD2");
        });

        LogEvent("OnRemoved", "Called when page is removed from stack - cleanup time!");
        _logCallback?.Invoke($"LifecycleAware #{_pageNumber}: OnRemoved() - Page cleaned up!");
    }

    #endregion

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LogEvent("MAUI Event", "OnAppearing (standard MAUI lifecycle)");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        LogEvent("MAUI Event", "OnDisappearing (standard MAUI lifecycle)");
    }

    private void LogEvent(string category, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _eventLog.AppendLine($"[{timestamp}] [{category}] {message}");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            EventLogLabel.Text = _eventLog.ToString();
            CallCountLabel.Text = $"{_callCount} total calls";
        });
    }

    private async void OnPushAnotherClicked(object sender, EventArgs e)
    {
        LogEvent("Action", "→ Pushing another LifecycleAwarePage");
        await Navigation.PushAsync(new LifecycleAwarePage(_pageNumber + 1, _logCallback), animated: true);
    }

    private async void OnPushDetailClicked(object sender, EventArgs e)
    {
        LogEvent("Action", "→ Pushing regular DetailPage");
        await Navigation.PushAsync(new DetailPage(_pageNumber + 100), animated: true);
    }

    private async void OnPopClicked(object sender, EventArgs e)
    {
        LogEvent("Action", "← Popping back");
        await Navigation.PopAsync(animated: true);
    }

    private async void OnPopToRootClicked(object sender, EventArgs e)
    {
        LogEvent("Action", "⏪ Popping to root");
        await Navigation.PopToRootAsync(animated: true);
    }
}
