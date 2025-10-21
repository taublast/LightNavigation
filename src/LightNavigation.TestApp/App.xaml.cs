using LightNavigation.TestApp.Pages;

namespace LightNavigation.TestApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Use LightNavigationPage instead of default AppShell
		MainPage = new LightNavigationPage(new MainPage());
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(MainPage!);
	}
}