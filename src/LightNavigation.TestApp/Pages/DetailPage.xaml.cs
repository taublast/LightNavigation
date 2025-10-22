using System.Text;

namespace LightNavigation.TestApp.Pages;

public partial class DetailPage : ContentPage
{
    private readonly int _pageNumber;
    private readonly StringBuilder _eventLog = new StringBuilder();
    private readonly DateTime _createdTime;

    // Pastel colors for better fade visibility
    private static readonly Color[] PastelColors = new[]
    {
        Color.FromRgb(255, 223, 223), // Light Pink
        Color.FromRgb(255, 239, 213), // Peach
        Color.FromRgb(255, 248, 220), // Light Yellow
        Color.FromRgb(230, 255, 230), // Light Green
        Color.FromRgb(224, 255, 255), // Light Cyan
        Color.FromRgb(230, 230, 250), // Lavender
        Color.FromRgb(255, 228, 225), // Misty Rose
        Color.FromRgb(240, 255, 240), // Honeydew
        Color.FromRgb(255, 250, 205), // Lemon Chiffon
        Color.FromRgb(230, 245, 255), // Light Blue
    };

    public DetailPage(int pageNumber)
    {
        InitializeComponent();

        _pageNumber = pageNumber;
        _createdTime = DateTime.Now;

        // Assign a random pastel background color
        var random = new Random(pageNumber); // Use pageNumber as seed for consistent colors per page number
        var backgroundColor = PastelColors[random.Next(PastelColors.Length)];
        this.BackgroundColor = backgroundColor;

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

    private async void OnPopToRootAnimatedClicked(object sender, EventArgs e)
    {
        LogEvent("⏪ Popping to root (animated)");
        await Navigation.PopToRootAsync(animated: true);
    }

    private async void OnPopToRootNoAnimClicked(object sender, EventArgs e)
    {
        LogEvent("⏪ Popping to root (no animation)");
        await Navigation.PopToRootAsync(animated: false);
    }
}
