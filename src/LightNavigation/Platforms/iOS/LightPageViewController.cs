#if IOS || MACCATALYST
using UIKit;
using Microsoft.Maui.Controls;

namespace LightNavigation.Platform
{
    public class LightPageViewController : UIViewController
    {
        public Page? MauiPage { get; set; }
    }
}
#endif
