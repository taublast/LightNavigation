using System.Text;
using LightNavigation.TestApp.Pages;

namespace LightNavigation.TestApp;

public partial class MainPage : ContentPage
{
    private readonly StringBuilder _logBuilder = new StringBuilder();
    private int _navigationCount = 0;
    private AnimationType _currentTransition = AnimationType.Default;

    public MainPage()
    {
        InitializeComponent();
        Log("MainPage initialized");

        // Set default values in pickers
        DefaultTransitionPicker.SelectedIndex = 0; // Default
        TransitionPicker.SelectedIndex = 0; // Default
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Log("MainPage: OnAppearing");
        UpdateStackInfo();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Log("MainPage: OnDisappearing");
    }

    private void UpdateStackInfo()
    {
        if (Navigation?.NavigationStack != null)
        {
            var stackCount = Navigation.NavigationStack.Count;
            StackCountLabel.Text = $"Stack Count: {stackCount}";
            CurrentPageLabel.Text = $"Current Page: {Navigation.NavigationStack.Last().GetType().Name}";
        }
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _logBuilder.AppendLine($"[{timestamp}] {message}");
        LogLabel.Text = _logBuilder.ToString();

        // Auto-scroll to bottom
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(50);
            var scrollView = LogLabel.Parent as ScrollView;
            if (scrollView != null)
            {
                await scrollView.ScrollToAsync(0, LogLabel.Height, false);
            }
        });
    }

    private void OnDefaultTransitionChanged(object sender, EventArgs e)
    {
        if (DefaultTransitionPicker.SelectedIndex == -1)
            return;

        var selected = DefaultTransitionPicker.SelectedItem.ToString();
        var transition = Enum.Parse<AnimationType>(selected);
        LightNavigationPage.SetDefaultTransition(transition);

        if (transition == AnimationType.Default)
        {
            GlobalDefaultLabel.Text = "Default (Platform Native)";
        }
        else
        {
            GlobalDefaultLabel.Text = $"{selected} (applies to all pages unless overridden)";
        }

        Log($"🌍 Global default transition set to: {selected}");
    }

    private void OnTransitionChanged(object sender, EventArgs e)
    {
        if (TransitionPicker.SelectedIndex == -1)
            return;

        var selected = TransitionPicker.SelectedItem.ToString();
        _currentTransition = Enum.Parse<AnimationType>(selected);
        CurrentTransitionLabel.Text = selected;

        if (_currentTransition == AnimationType.Default)
        {
            Log($"🎨 Per-page transition: {selected} (will use global default)");
        }
        else
        {
            Log($"🎨 Per-page transition: {selected} (overrides global default)");
        }
    }

    private Page CreateDetailPageWithTransition()
    {
        var page = new DetailPage(_navigationCount++);

        // Only set the attached property if user selected a specific override
        // If "Default" is selected, don't set it - let it use the global default
        if (_currentTransition != AnimationType.Default)
        {
            LightNavigationPage.SetTransition(page, _currentTransition);
        }

        return page;
    }

    private Page CreateLifecyclePageWithTransition()
    {
        var page = new LifecycleAwarePage(_navigationCount++, LogCallback);

        // Only set the attached property if user selected a specific override
        if (_currentTransition != AnimationType.Default)
        {
            LightNavigationPage.SetTransition(page, _currentTransition);
        }

        return page;
    }

    private async void OnNavigateDetailClicked(object sender, EventArgs e)
    {
        Log($"→ Navigating to DetailPage (animated) with {_currentTransition}");
        await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: true);
        UpdateStackInfo();
    }

    private async void OnNavigateDetailNoAnimClicked(object sender, EventArgs e)
    {
        Log($"→ Navigating to DetailPage (no animation) with {_currentTransition}");
        await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: false);
        UpdateStackInfo();
    }

    private async void OnNavigateAwareClicked(object sender, EventArgs e)
    {
        Log($"→ Navigating to LifecycleAwarePage with {_currentTransition}");
        await Navigation.PushAsync(CreateLifecyclePageWithTransition(), animated: true);
        UpdateStackInfo();
    }

    private async void OnPushThreePagesClicked(object sender, EventArgs e)
    {
        Log($"→ Pushing 3 pages in sequence with {_currentTransition}...");

        await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: true);
        await Task.Delay(300);

        await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: true);
        await Task.Delay(300);

        await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: true);

        Log($"✓ 3 pages pushed. Stack count: {Navigation.NavigationStack.Count}");
        UpdateStackInfo();
    }

    private async void OnPushFiveRapidlyClicked(object sender, EventArgs e)
    {
        Log($"→ Pushing 5 pages rapidly with {_currentTransition} (no delay)...");

        for (int i = 0; i < 5; i++)
        {
            await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: true);
            Log($"  Pushed page {i + 1}/5");
        }

        Log($"✓ 5 pages pushed. Stack count: {Navigation.NavigationStack.Count}");
        UpdateStackInfo();
    }

    private async void OnPopToRootAnimatedClicked(object sender, EventArgs e)
    {
        Log("→ PopToRoot (animated)...");
        await Navigation.PopToRootAsync(animated: true);
        Log($"✓ PopToRoot complete. Stack count: {Navigation.NavigationStack.Count}");
        UpdateStackInfo();
    }

    private async void OnPopToRootNoAnimClicked(object sender, EventArgs e)
    {
        Log("→ PopToRoot (no animation)...");
        await Navigation.PopToRootAsync(animated: false);
        Log($"✓ PopToRoot complete. Stack count: {Navigation.NavigationStack.Count}");
        UpdateStackInfo();
    }

    private async void OnRapidPushPopClicked(object sender, EventArgs e)
    {
        Log($"→ Starting rapid push/pop test with {_currentTransition} (10 cycles)...");

        for (int i = 0; i < 10; i++)
        {
            await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: true);
            Log($"  Cycle {i + 1}: Pushed");

            await Task.Delay(100);

            await Navigation.PopAsync(animated: true);
            Log($"  Cycle {i + 1}: Popped");

            await Task.Delay(100);
        }

        Log("✓ Rapid push/pop test completed!");
        UpdateStackInfo();
    }

    private async void OnRandomAnimationPushPopClicked(object sender, EventArgs e)
    {
        Log($"→ Starting random animation push/pop test with {_currentTransition} (20 cycles)...");
        var random = new Random();

        for (int i = 0; i < 20; i++)
        {
            var pushAnimated = random.Next(2) == 1;
            var popAnimated = random.Next(2) == 1;

            await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: pushAnimated);
            Log($"  Cycle {i + 1}: Pushed ({(pushAnimated ? "animated" : "no anim")})");

            await Task.Delay(50);

            await Navigation.PopAsync(animated: popAnimated);
            Log($"  Cycle {i + 1}: Popped ({(popAnimated ? "animated" : "no anim")})");

            await Task.Delay(50);
        }

        Log("✓ Random animation test completed!");
        UpdateStackInfo();
    }

    private async void OnRapidPopToRootClicked(object sender, EventArgs e)
    {
        Log($"→ Starting rapid PopToRoot test with {_currentTransition} (5 cycles)...");

        for (int cycle = 0; cycle < 5; cycle++)
        {
            Log($"  Cycle {cycle + 1}: Building deep stack (7 pages)...");

            // Build a deep stack
            for (int i = 0; i < 7; i++)
            {
                await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: false);
            }

            Log($"  Cycle {cycle + 1}: Stack built. Count: {Navigation.NavigationStack.Count}");
            await Task.Delay(100);

            // Rapid PopToRoot
            Log($"  Cycle {cycle + 1}: PopToRoot!");
            await Navigation.PopToRootAsync(animated: true);

            Log($"  Cycle {cycle + 1}: Back to root. Count: {Navigation.NavigationStack.Count}");
            await Task.Delay(200);
        }

        Log("✓ Rapid PopToRoot test completed!");
        UpdateStackInfo();
    }

    private async void OnMixedAnimationTortureClicked(object sender, EventArgs e)
    {
        Log($"→ Starting mixed animation torture test with {_currentTransition} (30 operations)...");
        var random = new Random();

        for (int i = 0; i < 30; i++)
        {
            var stackCount = Navigation.NavigationStack.Count;
            var operation = random.Next(3); // 0=push, 1=pop, 2=popToRoot

            if (stackCount == 1 || operation == 0)
            {
                // Push
                var animated = random.Next(2) == 1;
                await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: animated);
                Log($"  Op {i + 1}: PUSH ({(animated ? "anim" : "no anim")}) - Stack: {Navigation.NavigationStack.Count}");
            }
            else if (operation == 1 && stackCount > 1)
            {
                // Pop
                var animated = random.Next(2) == 1;
                await Navigation.PopAsync(animated: animated);
                Log($"  Op {i + 1}: POP ({(animated ? "anim" : "no anim")}) - Stack: {Navigation.NavigationStack.Count}");
            }
            else if (operation == 2 && stackCount > 3)
            {
                // PopToRoot (only if stack is deep enough)
                var animated = random.Next(2) == 1;
                await Navigation.PopToRootAsync(animated: animated);
                Log($"  Op {i + 1}: POP TO ROOT ({(animated ? "anim" : "no anim")}) - Stack: {Navigation.NavigationStack.Count}");
            }
            else
            {
                // Fallback to push
                var animated = random.Next(2) == 1;
                await Navigation.PushAsync(CreateDetailPageWithTransition(), animated: animated);
                Log($"  Op {i + 1}: PUSH ({(animated ? "anim" : "no anim")}) - Stack: {Navigation.NavigationStack.Count}");
            }

            await Task.Delay(random.Next(30, 100));
        }

        Log($"✓ Torture test completed! Final stack count: {Navigation.NavigationStack.Count}");
        UpdateStackInfo();
    }

    private void OnClearLogClicked(object sender, EventArgs e)
    {
        _logBuilder.Clear();
        LogLabel.Text = "Log cleared.";
    }

    // Callback for child pages to log to main page
    private void LogCallback(string message)
    {
        Log(message);
    }
}
