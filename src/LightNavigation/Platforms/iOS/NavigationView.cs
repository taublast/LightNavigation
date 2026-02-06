#if IOS || MACCATALYST
using UIKit;

namespace LightNavigation.Platform;

/// <summary>
/// Custom UIView that wraps a UINavigationController.
/// This allows MAUI to get the ViewController when needed.
/// </summary>
public class NavigationView : UIView
{
    private UINavigationController? _navigationController;

    public UINavigationController? NavigationController
    {
        get => _navigationController;
        set
        {
            if (_navigationController != value)
            {
                // Remove old navigation controller's view
                _navigationController?.View?.RemoveFromSuperview();

                _navigationController = value;

                // Add new navigation controller's view
                if (_navigationController?.View != null)
                {
                    AddSubview(_navigationController.View);
                    _navigationController.View.Frame = Bounds;
                    _navigationController.View.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
                }
            }
        }
    }

    public NavigationView()
    {
        AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
        BackgroundColor = UIColor.Clear;
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        // Ensure navigation controller's view fills this view
        if (_navigationController?.View != null)
        {
            _navigationController.View.Frame = Bounds;
        }
    }
}
#endif