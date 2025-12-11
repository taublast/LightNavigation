#if IOS || MACCATALYST
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UIKit;
using CoreAnimation;
using Debug = System.Diagnostics.Debug;
using MauiNavigationPage = Microsoft.Maui.Controls.NavigationPage;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace LightNavigation.Platform
{
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
                    _navigationController?.View.RemoveFromSuperview();

                    _navigationController = value;

                    // Add new navigation controller's view
                    if (_navigationController?.View != null)
                    {
                        AddSubview(_navigationController.View);
                        _navigationController.View.Frame = Bounds;
                        _navigationController.View.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
                    }
                }
            }
        }

        public NavigationView()
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
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

    /// <summary>
    /// Custom NavigationPage handler for iOS that eliminates flashing during navigation.
    ///
    /// CRITICAL: Must implement IPlatformViewHandler.ViewController property
    /// because MAUI's MapPrefersLargeTitles and MapIsNavigationBarTranslucent
    /// cast handler.ViewController to UINavigationController!
    ///
    /// APPROACH:
    /// - Uses UINavigationController for proper iOS navigation
    /// - Wraps it in a NavigationView (UIView) to satisfy ViewHandler<,UIView> constraint
    /// - Implements IPlatformViewHandler.ViewController to return the UINavigationController
    /// - Uses LightNavigationControllerDelegate and LightNavigationTransition for native custom transitions
    /// </summary>
    public class LightNavigationViewHandler : ViewHandler<LightNavigationPage, NavigationView>, IPlatformViewHandler
    {
        private const string TAG = "[LightNavigation_iOS]";

        private UINavigationController? _navigationController;
        private readonly List<UIViewController> _viewControllerStack = new();
        private readonly List<MauiPage> _pageStack = new();

        // Navigation operation queue to prevent concurrent operations and black screens
        private readonly Queue<Func<Task>> _navigationQueue = new();
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isProcessingQueue = false;

        // CRITICAL: IPlatformViewHandler requires this property!
        // MAUI casts this to UINavigationController in MapPrefersLargeTitles/MapIsNavigationBarTranslucent
        public new UIViewController? ViewController => _navigationController;

        public LightNavigationViewHandler() : base(PropertyMapper, CommandMapper)
        {
            var hashCode = GetHashCode();
            Debug.WriteLine($"{TAG} ✅ Handler created - HashCode: {hashCode}");
            Debug.WriteLine($"{TAG} 📋 CommandMapper registered");
        }

        /// <summary>
        /// Enqueues a navigation operation and processes the queue sequentially.
        /// This prevents concurrent navigation operations that cause crashes and black screens.
        /// </summary>
        private async Task EnqueueNavigationAsync(Func<Task> operation)
        {
            _navigationQueue.Enqueue(operation);
            Debug.WriteLine($"{TAG} 📥 Enqueued operation. Queue size: {_navigationQueue.Count}");

            // If already processing, just return - the queue will be processed
            if (_isProcessingQueue)
            {
                Debug.WriteLine($"{TAG} ⏳ Already processing queue, operation will be handled");
                return;
            }

            _isProcessingQueue = true;

            try
            {
                while (_navigationQueue.Count > 0)
                {
                    var nextOperation = _navigationQueue.Dequeue();
                    Debug.WriteLine($"{TAG} 🔄 Processing operation. Remaining: {_navigationQueue.Count}");

                    await _navigationSemaphore.WaitAsync();
                    try
                    {
                        await nextOperation();
                    }
                    finally
                    {
                        _navigationSemaphore.Release();
                    }
                }
            }
            finally
            {
                _isProcessingQueue = false;
                Debug.WriteLine($"{TAG} ✅ Queue processing complete");
            }
        }

        public static IPropertyMapper<LightNavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<LightNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
                //[Microsoft.Maui.Controls.NavigationPage.HasNavigationBarProperty.PropertyName] = MapHasNavigationBar
            };

        public static CommandMapper<LightNavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<LightNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        private static void MapHasNavigationBar(LightNavigationViewHandler handler, LightNavigationPage view)
        {
            if (handler._navigationController != null)
            {
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(view);
                handler._navigationController.NavigationBarHidden = !hasNavBar;
                Debug.WriteLine($"{TAG} 🔵 NavigationBar visibility changed: {hasNavBar}");
            }
        }

        protected override NavigationView CreatePlatformView()
        {
            // Create UINavigationController
            _navigationController = new UINavigationController();
            
            // Assign our custom delegate to handle transitions
            _navigationController.Delegate = new LightNavigationControllerDelegate();
            Debug.WriteLine($"{TAG} ✅ Assigned LightNavigationControllerDelegate");

            // Set initial NavigationBar visibility based on VirtualView
            // Default is visible unless explicitly hidden
            if (VirtualView != null)
            {
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(VirtualView);
                _navigationController.NavigationBarHidden = !hasNavBar;
                Debug.WriteLine($"{TAG} 🔵 NavigationBar visibility: {hasNavBar}");
            }
            else
            {
                // Default to visible
                _navigationController.NavigationBarHidden = false;
            }

            // Return the navigation controller's View wrapped in NavigationView
            var navigationView = new NavigationView
            {
                NavigationController = _navigationController
            };

            return navigationView;
        }

        protected override void ConnectHandler(NavigationView platformView)
        {
            base.ConnectHandler(platformView);

            // Subscribe to navigation events from the NavigationPage
            if (VirtualView is INavigationPageController navController)
            {
                navController.PushRequested += OnPushRequested;
                navController.PopRequested += OnPopRequested;
                navController.PopToRootRequested += OnPopToRootRequested;
                Debug.WriteLine($"{TAG} ✅ Subscribed to navigation events");
            }
            else
            {
                Debug.WriteLine($"{TAG} ⚠️ VirtualView is NOT INavigationPageController!");
            }

            // Subscribe to property changes for BarTextColor, etc.
            if (VirtualView != null)
            {
                VirtualView.PropertyChanged += OnNavigationPagePropertyChanged;
            }

            // Manually show initial page if we have one
            if (VirtualView?.CurrentPage != null && MauiContext != null)
            {
                _ = EnqueueNavigationAsync(async () =>
                {
                    await ShowPageAsync(VirtualView.CurrentPage, false, AnimationType.Default, isInitial: true);
                });
            }
            else
            {
                Debug.WriteLine($"{TAG} ⚠️ No CurrentPage to show!");
            }
        }

        protected virtual void SetupNewPage(UINavigationController navigationController, Page page,
            UIViewController viewController)
        {
            // IMPORTANT: Update navigation bar visibility for the initial page
            // The property mapper might not have run yet during initial setup
            if (navigationController != null && page!=null)
            {

                var pageView = page.Handler.PlatformView as UIView;
                // We need to manually apply BackgroundColor to the MAUI page
                if (pageView != null && page.BackgroundColor != null)
                {
                    var mauiBgColor = page.BackgroundColor;
                    var nativeColor = mauiBgColor.ToPlatform();
                    pageView.BackgroundColor = nativeColor;
                    Debug.WriteLine($"{TAG} 🔵 Applied background color: {mauiBgColor} -> {nativeColor}");
                }
                else
                {
                    Debug.WriteLine($"{TAG} ⚠️ Page BackgroundColor is null, using default");
                }

                if (viewController != null)
                {
                    viewController.NavigationItem.Title = page.Title;
                    Debug.WriteLine($"{TAG} 🔵 Set navigation title: {page.Title}");
                }

                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(page);
                navigationController.NavigationBarHidden = !hasNavBar;
                Debug.WriteLine($"{TAG} 🔵 Initial page NavigationBar visibility: {hasNavBar}");
                if (hasNavBar)
                {
                    UpdateBarTextColor();
                }
            }
        }
        
        protected override void DisconnectHandler(NavigationView platformView)
        {
            // Unsubscribe from navigation events
            if (VirtualView is INavigationPageController navController)
            {
                navController.PushRequested -= OnPushRequested;
                navController.PopRequested -= OnPopRequested;
                navController.PopToRootRequested -= OnPopToRootRequested;
            }

            // Unsubscribe from property changes
            if (VirtualView != null)
            {
                VirtualView.PropertyChanged -= OnNavigationPagePropertyChanged;
            }

            // Clean up
            _navigationQueue.Clear();
            _viewControllerStack.Clear();
            _pageStack.Clear();
            _navigationController?.View.RemoveFromSuperview();
            _navigationController = null;
            _navigationSemaphore?.Dispose();

            base.DisconnectHandler(platformView);
        }

        private async void OnPushRequested(object? sender, NavigationRequestedEventArgs e)
        {
            var tcs = new TaskCompletionSource<bool>();
            e.Task = tcs.Task;

            await EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var transition = LightNavigationPage.GetEffectiveTransition(e.Page);
                    Debug.WriteLine($"{TAG} 🟢 OnPushRequested - Page: {e.Page.GetType().Name}, Animated: {e.Animated}, Transition: {transition}");
                    await ShowPageAsync(e.Page, e.Animated, transition, isInitial: false);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ OnPushRequested error: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });
        }

        private async void OnPopRequested(object? sender, NavigationRequestedEventArgs e)
        {
            Debug.WriteLine($"{TAG} 🟢 OnPopRequested - Animated: {e.Animated}");

            var tcs = new TaskCompletionSource<bool>();
            e.Task = tcs.Task;

            await EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var poppingPage = _pageStack.LastOrDefault();
                    var transition = poppingPage != null
                        ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                        : AnimationType.Default;

                    await PopPageAsync(e.Animated, transition);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ OnPopRequested error: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });
        }

        private async void OnPopToRootRequested(object? sender, NavigationRequestedEventArgs e)
        {
            Debug.WriteLine($"{TAG} 🟢 OnPopToRootRequested - Animated: {e.Animated}");

            var tcs = new TaskCompletionSource<bool>();
            e.Task = tcs.Task;

            await EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var poppingPage = _pageStack.LastOrDefault();
                    var transition = poppingPage != null
                        ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                        : AnimationType.Default;

                    await PopToRootAsync(e.Animated, transition);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ OnPopToRootRequested error: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });
        }

        private static void MapRequestNavigation(LightNavigationViewHandler handler, LightNavigationPage view, object? args)
        {
            if (args is not NavigationRequest request)
            {
                Debug.WriteLine($"{TAG} ⚠️ Invalid args - not NavigationRequest");
                return;
            }

            Debug.WriteLine($"{TAG} 🟢 Calling HandleNavigationRequest with {request.NavigationStack.Count} pages, Animated: {request.Animated}");
            handler.HandleNavigationRequest(request);
        }

        private void HandleNavigationRequest(NavigationRequest request)
        {
            _ = EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var newStack = request.NavigationStack;
                    var currentStackCount = _viewControllerStack.Count;

                    Debug.WriteLine($"{TAG} 📊 Current: {currentStackCount}, New: {newStack.Count}");

                    if (newStack.Count > currentStackCount)
                    {
                        // Push - show the new page
                        var newPage = newStack[newStack.Count - 1] as MauiPage;
                        if (newPage != null)
                        {
                            // Get the effective transition for this page
                            var transition = LightNavigationPage.GetEffectiveTransition(newPage);

                            if (currentStackCount == 0)
                            {
                                Debug.WriteLine($"{TAG} ➡️ Initial page: {newPage.GetType().Name}");
                                await ShowPageAsync(newPage, false, transition, isInitial: true);
                            }
                            else
                            {
                                Debug.WriteLine($"{TAG} ➡️ Push: {newPage.GetType().Name}, Transition: {transition}");
                                await ShowPageAsync(newPage, request.Animated, transition, isInitial: false);
                            }
                        }
                    }
                    else if (newStack.Count < currentStackCount)
                    {
                        // Pop
                        var diff = currentStackCount - newStack.Count;

                        // Get the transition from the page being popped (last page in current stack)
                        var poppingPage = _pageStack.LastOrDefault();
                        var transition = poppingPage != null
                            ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                            : AnimationType.Default;

                        if (diff == currentStackCount - 1)
                        {
                            // Pop to root
                            await PopToRootAsync(request.Animated, transition);
                        }
                        else
                        {
                            // Pop one or more pages
                            for (int i = 0; i < diff; i++)
                            {
                                await PopPageAsync(request.Animated, transition);
                            }
                        }
                    }

                    // Notify MAUI that navigation is complete
                    if (VirtualView is IStackNavigation nav)
                    {
                        nav.NavigationFinished(newStack);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ Error: {ex.Message}");
                }
            });
        }

        private Task ShowPageAsync(MauiPage page, bool animate, AnimationType transition, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                Debug.WriteLine($"{TAG} 🔵 ShowPageAsync called: page={page.GetType().Name}, animate={animate}, transition={transition}, isInitial={isInitial}");

                if (MauiContext == null)
                {
                    Debug.WriteLine($"{TAG} ❌ MauiContext is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                if (_navigationController == null)
                {
                    Debug.WriteLine($"{TAG} ❌ NavigationController is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                // Create view and wrap in LightPageViewController
                var pageView = page.ToPlatform(MauiContext);
                
                if (pageView == null)
                {
                    Debug.WriteLine($"{TAG} ❌ ToPlatform returned null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                // Use our custom ViewController that holds the MauiPage reference
                // This is CRITICAL for the Delegate to find the transition settings
                var viewController = new LightPageViewController();
                viewController.View = pageView;
                viewController.MauiPage = page;

                Debug.WriteLine($"{TAG} 🔵 Created LightPageViewController");

                // Get old page and invoke INavigationAware
                var oldPage = _pageStack.Count > 0 ? _pageStack.Last() : null;
                var newAware = page as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                newAware?.OnPushing();
                oldAware?.OnPopping();

                if (isInitial)
                {
                    // Set as root - no animation
                    _navigationController.SetViewControllers(new[] { viewController }, false);
                    _viewControllerStack.Add(viewController);

                    SetupNewPage(_navigationController, page, viewController);

                    _pageStack.Add(page);
                    oldAware?.OnCovered();
                    newAware?.OnTopmost();

                    tcs.SetResult(true);
                }
                else
                {
                    SetupNewPage(_navigationController, page, viewController);

                    // Determine if we should animate
                    bool actuallyAnimate = animate && transition != AnimationType.None;
                    
                    Debug.WriteLine($"{TAG} ➡️ Pushing ViewController, Animate: {actuallyAnimate}");
                    
                    if (actuallyAnimate)
                    {
                        CATransaction.Begin();
                        CATransaction.CompletionBlock = () =>
                        {
                            Debug.WriteLine($"{TAG} ✅ Push animation complete");
                            tcs.TrySetResult(true);
                        };
                    }

                    // Push the view controller
                    // The Delegate will handle the custom transition if actuallyAnimate is true
                    _navigationController.PushViewController(viewController, actuallyAnimate);
                    
                    if (actuallyAnimate)
                    {
                        CATransaction.Commit();
                    }
                    else
                    {
                        tcs.SetResult(true);
                    }
                    
                    _viewControllerStack.Add(viewController);
                    _pageStack.Add(page);

                    // Update navbar visibility based on the page's attached property
                    UpdateNavigationBarVisibility(page);

                    newAware?.OnTopmost();
                    oldAware?.OnCovered();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in ShowPageAsync: {ex.Message}");
                Debug.WriteLine($"{TAG} ❌ Stack trace: {ex.StackTrace}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopPageAsync(bool animate, AnimationType transition)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewControllerStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ⚠️ Cannot pop - only root page");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                if (_navigationController == null)
                {
                    Debug.WriteLine($"{TAG} ❌ NavigationController is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} ⬅️ Popping, animate: {animate}, transition: {transition}");

                // Get pages for INavigationAware
                var oldPage = _pageStack.Count > 0 ? _pageStack.Last() : null;
                var newPage = _pageStack.Count > 1 ? _pageStack[_pageStack.Count - 2] : null;

                var newAware = newPage as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                bool actuallyAnimate = animate && transition != AnimationType.None;

                if (actuallyAnimate)
                {
                    CATransaction.Begin();
                    CATransaction.CompletionBlock = () =>
                    {
                        Debug.WriteLine($"{TAG} ✅ Pop animation complete");
                        tcs.TrySetResult(true);
                    };
                }

                // Pop the view controller
                // The Delegate will handle the custom transition if actuallyAnimate is true
                _navigationController.PopViewController(actuallyAnimate);

                if (actuallyAnimate)
                {
                    CATransaction.Commit();
                }
                else
                {
                    tcs.SetResult(true);
                }

                if (_viewControllerStack.Count > 0)
                {
                    _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                }

                if (_pageStack.Count > 0)
                {
                    _pageStack.RemoveAt(_pageStack.Count - 1);
                }

                oldAware?.OnRemoved();
                newAware?.OnTopmost();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ PopPageAsync error: {ex.Message}");
                Debug.WriteLine($"{TAG} Stack trace: {ex.StackTrace}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopToRootAsync(bool animate, AnimationType transition)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewControllerStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ℹ️ Already at root");
                    tcs.SetResult(true);
                    return tcs.Task;
                }

                if (_navigationController == null)
                {
                    Debug.WriteLine($"{TAG} ❌ NavigationController is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} 🏠 PopToRoot, animate: {animate}");

                // Get references BEFORE popping
                var rootViewController = _viewControllerStack.First();
                var rootPage = _pageStack.First();

                // Get all pages that will be removed (for INavigationAware)
                var removedPages = _pageStack.Skip(1).ToList();

                bool actuallyAnimate = animate && transition != AnimationType.None;

                if (actuallyAnimate)
                {
                    CATransaction.Begin();
                    CATransaction.CompletionBlock = () =>
                    {
                        Debug.WriteLine($"{TAG} ✅ PopToRoot animation complete");
                        tcs.TrySetResult(true);
                    };
                }

                // Pop to root
                _navigationController.PopToRootViewController(actuallyAnimate);

                if (actuallyAnimate)
                {
                    CATransaction.Commit();
                }
                else
                {
                    tcs.SetResult(true);
                }

                // Update stacks
                _viewControllerStack.Clear();
                _viewControllerStack.Add(rootViewController);

                _pageStack.Clear();
                _pageStack.Add(rootPage);

                // Notify INavigationAware - all removed pages
                foreach (var removedPage in removedPages)
                {
                    if (removedPage is INavigationAware aware)
                    {
                        aware.OnRemoved();
                    }
                }

                // Root page is now topmost
                if (rootPage is INavigationAware rootAware)
                {
                    rootAware.OnTopmost();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ PopToRootAsync error: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        // Property change handler for NavigationPage properties
        private void OnNavigationPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == Microsoft.Maui.Controls.NavigationPage.BarTextColorProperty.PropertyName)
            {
                UpdateBarTextColor();
            }
            else if (e.PropertyName == Microsoft.Maui.Controls.NavigationPage.BarBackgroundColorProperty.PropertyName)
            {
                UpdateBarBackground();
            }
        }

        // Copied from MAUI NavigationRenderer - handles BarTextColor styling
        private void UpdateBarTextColor()
        {
            if (_navigationController?.NavigationBar == null || VirtualView == null)
                return;

            var barTextColor = VirtualView.BarTextColor;

            // Determine new title text attributes via global static data
            var globalTitleTextAttributes = UINavigationBar.Appearance.TitleTextAttributes;
            var titleTextAttributes = new UIKit.UIStringAttributes
            {
                ForegroundColor = barTextColor == null ? globalTitleTextAttributes?.ForegroundColor : barTextColor.ToPlatform(),
                Font = globalTitleTextAttributes?.Font
            };

            // Determine new large title text attributes via global static data
            var largeTitleTextAttributes = titleTextAttributes;
            if (OperatingSystem.IsIOSVersionAtLeast(11))
            {
                var globalLargeTitleTextAttributes = UINavigationBar.Appearance.LargeTitleTextAttributes;

                largeTitleTextAttributes = new UIKit.UIStringAttributes
                {
                    ForegroundColor = barTextColor == null ? globalLargeTitleTextAttributes?.ForegroundColor : barTextColor.ToPlatform(),
                    Font = globalLargeTitleTextAttributes?.Font
                };
            }

            if (OperatingSystem.IsIOSVersionAtLeast(13))
            {
                if (_navigationController.NavigationBar.CompactAppearance != null)
                {
                    _navigationController.NavigationBar.CompactAppearance.TitleTextAttributes = titleTextAttributes;
                    _navigationController.NavigationBar.CompactAppearance.LargeTitleTextAttributes = largeTitleTextAttributes;
                }

                if (_navigationController.NavigationBar.StandardAppearance != null)
                {
                    _navigationController.NavigationBar.StandardAppearance.TitleTextAttributes = titleTextAttributes;
                    _navigationController.NavigationBar.StandardAppearance.LargeTitleTextAttributes = largeTitleTextAttributes;
                }

                if (_navigationController.NavigationBar.ScrollEdgeAppearance != null)
                {
                    _navigationController.NavigationBar.ScrollEdgeAppearance.TitleTextAttributes = titleTextAttributes;
                    _navigationController.NavigationBar.ScrollEdgeAppearance.LargeTitleTextAttributes = largeTitleTextAttributes;
                }
            }
            else
            {
                _navigationController.NavigationBar.TitleTextAttributes = titleTextAttributes;

                if (OperatingSystem.IsIOSVersionAtLeast(11))
                    _navigationController.NavigationBar.LargeTitleTextAttributes = largeTitleTextAttributes;
            }

            // Set TintColor (i.e. Back Button arrow and Text)
            var iconColor = barTextColor;

            _navigationController.NavigationBar.TintColor = iconColor == null
                ? UINavigationBar.Appearance.TintColor
                : iconColor.ToPlatform();

            Debug.WriteLine($"{TAG} 🎨 Updated BarTextColor to: {barTextColor}");
        }

        // Placeholder for bar background (can be implemented later)
        private void UpdateBarBackground()
        {
            // TODO: Implement if needed
            Debug.WriteLine($"{TAG} 🎨 UpdateBarBackground called");
        }

        // Update navigation bar visibility based on the current page's attached property
        private void UpdateNavigationBarVisibility(Page? page)
        {
            if (_navigationController == null)
                return;

            if (page != null)
            {
                // Check the attached property on the page
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(page);
                _navigationController.SetNavigationBarHidden(!hasNavBar, false);
                Debug.WriteLine($"{TAG} 🔵 Updated NavigationBar visibility for {page.GetType().Name}: {hasNavBar}");
            }
        }
    }
}
#endif
