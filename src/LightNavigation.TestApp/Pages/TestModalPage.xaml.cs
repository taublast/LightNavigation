namespace LightNavigation.TestApp.Pages;

public partial class TestModalPage : ContentPage
{
    public TestModalPage()
    {
        InitializeComponent();
    }

    private async void OnPopModalClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}