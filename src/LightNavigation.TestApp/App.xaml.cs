using LightNavigation.TestApp.Pages;

namespace LightNavigation.TestApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
        // Use LightNavigationPage instead of default AppShell
		return new Window(new LightNavigationPage(new MainPage()));
	}
}