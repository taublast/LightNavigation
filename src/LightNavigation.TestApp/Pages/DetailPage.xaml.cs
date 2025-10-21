using System.Text;

namespace LightNavigation.TestApp.Pages;

public partial class DetailPage : ContentPage
{
    private readonly int _pageNumber;
    private readonly StringBuilder _eventLog = new StringBuilder();
    private readonly DateTime _createdTime;

    public DetailPage(int pageNumber)
    {
        InitializeComponent();

        _pageNumber = pageNumber;
        _createdTime = DateTime.Now;

        TitleLabel.Text = $"📄 Detail Page #{pageNumber}";
        PageNumberLabel.Text = $"Page #{pageNumber}";
        CreatedTimeLabel.Text = _createdTime.ToString("HH:mm:ss");

        LogEvent("Page constructed");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LogEvent("OnAppearing");
        UpdateStackInfo();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        LogEvent("OnDisappearing");
    }

    private void UpdateStackInfo()
    {
        if (Navigation?.NavigationStack != null)
        {
            var stack = Navigation.NavigationStack.ToList();
            var position = stack.IndexOf(this) + 1;

            StackPositionLabel.Text = $"{position} of {stack.Count}";
            StackCountLabel.Text = stack.Count.ToString();
        }
    }

    private void LogEvent(string eventName)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _eventLog.AppendLine($"[{timestamp}] {eventName}");
        EventLogLabel.Text = _eventLog.ToString();
    }

    private async void OnPushAnotherClicked(object sender, EventArgs e)
    {
        LogEvent("→ Pushing another page");

        // Find the MainPage to get the navigation counter
        var mainPage = Navigation.NavigationStack.FirstOrDefault() as MainPage;
        int nextNumber = _pageNumber + 1;

        await Navigation.PushAsync(new DetailPage(nextNumber), animated: true);
        LogEvent($"✓ Pushed DetailPage #{nextNumber}");
    }

    private async void OnPopClicked(object sender, EventArgs e)
    {
        LogEvent("← Popping (animated)");
        await Navigation.PopAsync(animated: true);
    }

    private async void OnPopNoAnimClicked(object sender, EventArgs e)
    {
        LogEvent("← Popping (no animation)");
        await Navigation.PopAsync(animated: false);
    }

    private async void OnPopToRootClicked(object sender, EventArgs e)
    {
        LogEvent("⏪ Popping to root");
        await Navigation.PopToRootAsync(animated: true);
    }
}
