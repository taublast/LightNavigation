#if IOS || MACCATALYST
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Controls.PlatformConfiguration;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UIKit;
using CoreGraphics;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using Debug = System.Diagnostics.Debug;
using MauiNavigationPage = Microsoft.Maui.Controls.NavigationPage;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace LightNavigation.Platform
{
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
        private const double ANIMATION_IN_DURATION = 0.3;
        private const double ANIMATION_OUT_DURATION = 0.3;
        private const double WHIRL3_DURATION = 0.4;

        private UINavigationController? _navigationController;
        private readonly List<UIViewController> _viewControllerStack = new();
        private readonly List<MauiPage> _pageStack = new();

        // Navigation operation queue to prevent concurrent operations and black screens
        private readonly Queue<Func<Task>> _navigationQueue = new();
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isProcessingQueue = false;

        public static IPropertyMapper<LightNavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<LightNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
                [Microsoft.Maui.Controls.NavigationPage.HasNavigationBarProperty.PropertyName] = MapHasNavigationBar
            };

        public static CommandMapper<LightNavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<LightNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        // CRITICAL: IPlatformViewHandler requires this property!
        // MAUI casts this to UINavigationController in MapPrefersLargeTitles/MapIsNavigationBarTranslucent
        public new UIViewController? ViewController => _navigationController;

        public LightNavigationViewHandler() : base(PropertyMapper, CommandMapper)
        {
            var hashCode = GetHashCode();
            Debug.WriteLine($"{TAG} ✅ Handler created - HashCode: {hashCode}");
            Debug.WriteLine($"{TAG} 📋 CommandMapper registered");
        }

        void UpdateSetNavigationBarForPage(MauiPage page, bool animated)
        {
            if (_navigationController != null && page != null)
            {
                var hasNavBar = MauiNavigationPage.GetHasNavigationBar(page);
                _navigationController.SetNavigationBarHidden(!hasNavBar, animated);
            }
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
        
        private static void MapHasNavigationBar(LightNavigationViewHandler handler, LightNavigationPage view)
        {
            if (view != null)
            {
                bool hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(view);
                if (handler._navigationController != null)
                {
                    if (view.CurrentPage != null)
                    {
                        //prioritize page setting
                        hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(view.CurrentPage);
                    }

                    handler._navigationController.SetNavigationBarHidden(!hasNavBar, false);
                    //handler._navigationController.NavigationBarHidden = !hasNavBar;
                    Debug.WriteLine($"{TAG} 🔵 NavigationBar visibility changed: {hasNavBar}");
                }
            }
        }

        protected override NavigationView CreatePlatformView()
        {
            _navigationController = new UINavigationController();
            _navigationController.Delegate = new LightNavigationControllerDelegate();
            _navigationController.NavigationBarHidden = this.VirtualView != null 
                                                        && !Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar((BindableObject)this.VirtualView);

            return new NavigationView()
            {
                NavigationController = this._navigationController
            };
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

        protected virtual void SetupNewPage(
            UINavigationController navigationController,
            Microsoft.Maui.Controls.Page page,
            UIViewController viewController)
        {
            if (navigationController == null || page == null)
                return;
            if (page.Handler.PlatformView is UIView platformView && page.BackgroundColor != null)
            {
                UIColor platform = page.BackgroundColor.ToPlatform();
                platformView.BackgroundColor = platform;
            }
            if (viewController != null)
                viewController.NavigationItem.Title = page.Title;

            bool hasNavigationBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar((BindableObject)page);
            navigationController.NavigationBarHidden = !hasNavigationBar;
            
            if (!hasNavigationBar)
                return;

            this.UpdateBarTextColor();
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

            // Clean up - clear MauiPage references to prevent memory leaks
            foreach (var vc in _viewControllerStack)
            {
                if (vc is LightPageViewController lightPageVC)
                {
                    lightPageVC.MauiPage = null;
                }
            }

            _navigationQueue.Clear();
            _viewControllerStack.Clear();
            _pageStack.Clear();

            // Clear delegate to break potential retain cycles
            if (_navigationController != null)
            {
                _navigationController.Delegate = null;
                _navigationController.View?.RemoveFromSuperview();
            }
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

                //Make page fullscreen if needed
                //if (!page.On<iOS>().UsingSafeArea())
                //{
                //    page.On<iOS>().SetSafeAreaInsets(new Thickness(0.0));
                //}

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

                    // Safe to update navbar here - page is already in the nav stack
                    SetupNewPage(_navigationController, page, viewController);

                    _pageStack.Add(page);

                    //not working
                    UpdateSetNavigationBarForPage(page, false);
                    //UpdateNavigationBarVisibility(page);

                    oldAware?.OnCovered();
                    newAware?.OnTopmost();

                    tcs.SetResult(true);
                }
                else
                {
                    // Set bg color and title BEFORE push (safe - doesn't affect current page)
                    // But do NOT update navbar yet - that would change the CURRENT page's navbar
                    SetupNewPage(_navigationController, page, viewController);

                    // Use native UINavigationController push animation
                    // This properly handles navbar transitions between pages
                    Debug.WriteLine($"{TAG} ➡️ Pushing ViewController, Animate: {animate}, Transition: {transition}");
                    _navigationController.PushViewController(viewController, animate);
                    _viewControllerStack.Add(viewController);
                    _pageStack.Add(page);

                    //not working
                    //UpdateNavigationBarVisibility(page);

                    newAware?.OnTopmost();
                    oldAware?.OnCovered();

                    if (animate)
                    {
                        // CRITICAL: Wait for native push animation to complete before resolving TCS.
                        // If TCS resolves immediately, the next pop can start while the push animation
                        // is still running (100ms delay < 300ms animation), causing the pop's manual
                        // animation to fight with iOS's still-running push animation (visual chaos).
                        var coordinator = viewController.GetTransitionCoordinator();
                        if (coordinator != null)
                        {
                            coordinator.AnimateAlongsideTransition((context) => { }, (context) =>
                            {
                                Debug.WriteLine($"{TAG} ✅ Push animation complete");
                                tcs.TrySetResult(true);
                            });
                        }
                        else
                        {
                            tcs.SetResult(true);
                        }
                    }
                    else
                    {
                        tcs.SetResult(true);
                    }
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

                var oldAware = (_pageStack.Count > 0 ? _pageStack.Last() : null) as INavigationAware;
                var newAware = (_pageStack.Count > 1 ? _pageStack[_pageStack.Count - 2] : null) as INavigationAware;

                if (animate)
                {
                    // Use native iOS pop animation for ALL transitions
                    // This properly handles navbar transitions (no jumping)
                    // Track completion via transition coordinator (reliable, unlike CATransaction)
                    _navigationController.PopViewController(true);

                    var newTopVC = _navigationController.TopViewController;
                    var coordinator = newTopVC?.GetTransitionCoordinator();
                    if (coordinator != null)
                    {
                        coordinator.AnimateAlongsideTransition((ctx) => { }, (ctx) =>
                        {
                            Debug.WriteLine($"{TAG} ✅ Pop animation complete");
                            PopCleanupStacks(oldAware, newAware);
                            tcs.TrySetResult(true);
                        });
                    }
                    else
                    {
                        PopCleanupStacks(oldAware, newAware);
                        tcs.SetResult(true);
                    }
                }
                else
                {
                    _navigationController.PopViewController(false);
                    PopCleanupStacks(oldAware, newAware);
                    tcs.SetResult(true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ PopPageAsync error: {ex.Message}");
                Debug.WriteLine($"{TAG} Stack trace: {ex.StackTrace}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private void PopCleanupStacks(INavigationAware? oldAware, INavigationAware? newAware)
        {
            if (_viewControllerStack.Count > 0)
            {
                var poppedVC = _viewControllerStack[_viewControllerStack.Count - 1];
                _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                if (poppedVC is LightPageViewController lightPageVC)
                {
                    lightPageVC.MauiPage = null;
                }
            }

            if (_pageStack.Count > 0)
            {
                _pageStack.RemoveAt(_pageStack.Count - 1);
            }

            oldAware?.OnRemoved();
            newAware?.OnTopmost();
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

                Debug.WriteLine($"{TAG} 🏠 PopToRoot, animate: {animate}, stack: {_viewControllerStack.Count}");

                var rootViewController = _viewControllerStack.First();
                var rootPage = _pageStack.First();
                var removedPages = _pageStack.Skip(1).ToList();
                var removedViewControllers = _viewControllerStack.Skip(1).ToList();

                if (animate)
                {
                    // Use native iOS pop-to-root animation for ALL transitions
                    _navigationController.PopToRootViewController(true);

                    var newTopVC = _navigationController.TopViewController;
                    var coordinator = newTopVC?.GetTransitionCoordinator();
                    if (coordinator != null)
                    {
                        coordinator.AnimateAlongsideTransition((ctx) => { }, (ctx) =>
                        {
                            Debug.WriteLine($"{TAG} ✅ PopToRoot animation complete");
                            PopToRootCleanupStacks(rootViewController, rootPage, removedViewControllers, removedPages);
                            tcs.TrySetResult(true);
                        });
                    }
                    else
                    {
                        PopToRootCleanupStacks(rootViewController, rootPage, removedViewControllers, removedPages);
                        tcs.SetResult(true);
                    }
                }
                else
                {
                    _navigationController.PopToRootViewController(false);
                    PopToRootCleanupStacks(rootViewController, rootPage, removedViewControllers, removedPages);
                    tcs.SetResult(true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ PopToRootAsync error: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private void PopToRootCleanupStacks(UIViewController rootViewController, MauiPage rootPage,
            List<UIViewController> removedViewControllers, List<MauiPage> removedPages)
        {
            _viewControllerStack.Clear();
            _viewControllerStack.Add(rootViewController);

            _pageStack.Clear();
            _pageStack.Add(rootPage);

            foreach (var vc in removedViewControllers)
            {
                if (vc is LightPageViewController lightPageVC)
                {
                    lightPageVC.MauiPage = null;
                }
            }

            foreach (var removedPage in removedPages)
            {
                if (removedPage is INavigationAware aware)
                {
                    aware.OnRemoved();
                }
            }

            if (rootPage is INavigationAware rootAware)
            {
                rootAware.OnTopmost();
            }
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
        private void UpdateNavigationBarVisibility(MauiPage? page)
        {
            if (this._navigationController == null || page == null)
                return;
            this._navigationController.SetNavigationBarHidden(!Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar((BindableObject)page), false);


            //if (_navigationController.TopViewController != null)
            //{
            //    _navigationController.TopViewController.View?.SetNeedsLayout();
            //    _navigationController.TopViewController.View?.LayoutIfNeeded();

            //    // Trigger safe area update
            //    if (OperatingSystem.IsIOSVersionAtLeast(11))
            //    {
            //        _navigationController.TopViewController.ViewSafeAreaInsetsDidChange();
            //    }
            //}
        }

        private double GetAnimationDuration(MauiPage? page, bool isPush)
        {
            if (page != null)
            {
                var customSpeed = LightNavigationPage.GetTransitionSpeed(page);
                if (customSpeed > 0)
                {
                    return customSpeed / 1000.0;
                }

                var transition = LightNavigationPage.GetEffectiveTransition(page);
                if (transition == AnimationType.WhirlIn3)
                {
                    return WHIRL3_DURATION;
                }
            }

            return isPush ? ANIMATION_IN_DURATION : ANIMATION_OUT_DURATION;
        }

        private UIViewAnimationCurve GetAnimationCurve(MauiPage? page, bool isPush)
        {
            if (page != null)
            {
                var easing = LightNavigationPage.GetTransitionEasing(page);

                switch (easing)
                {
                    case TransitionEasing.Linear:
                        return UIViewAnimationCurve.Linear;
                    case TransitionEasing.Decelerate:
                        return UIViewAnimationCurve.EaseOut;
                    case TransitionEasing.Accelerate:
                        return UIViewAnimationCurve.EaseIn;
                    case TransitionEasing.AccelerateDecelerate:
                        return UIViewAnimationCurve.EaseInOut;
                }
            }

            return isPush ? UIViewAnimationCurve.EaseOut : UIViewAnimationCurve.EaseIn;
        }

        private void ApplyPopAnimationStart(UIView oldView, UIView newView, CGRect containerBounds, AnimationType transition)
        {
            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    break;

                case AnimationType.SlideFromLeft:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    break;

                case AnimationType.SlideFromBottom:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    break;

                case AnimationType.SlideFromTop:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    break;

                case AnimationType.ParallaxSlideFromRight:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(-containerBounds.Width * 0.3, 0, containerBounds.Width, containerBounds.Height);
                    break;

                case AnimationType.ParallaxSlideFromLeft:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(containerBounds.Width * 0.3, 0, containerBounds.Width, containerBounds.Height);
                    break;

                case AnimationType.Fade:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Alpha = 1;
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    newView.Alpha = 0;
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Transform = CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ZoomIn:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ZoomOut:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.WhirlIn:
                case AnimationType.WhirlIn3:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.None:
                    oldView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    oldView.Transform = CGAffineTransform.MakeIdentity();
                    newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                    newView.Transform = CGAffineTransform.MakeIdentity();
                    break;
            }
        }

        private Action CreatePopAnimation(UIView oldView, UIView newView, CGRect containerBounds, AnimationType transition)
        {
            return () =>
            {
                switch (transition)
                {
                    case AnimationType.Default:
                    case AnimationType.SlideFromRight:
                        oldView.Frame = new CGRect(containerBounds.Width, 0, containerBounds.Width, containerBounds.Height);
                        break;

                    case AnimationType.SlideFromLeft:
                        oldView.Frame = new CGRect(-containerBounds.Width, 0, containerBounds.Width, containerBounds.Height);
                        break;

                    case AnimationType.SlideFromBottom:
                        oldView.Frame = new CGRect(0, containerBounds.Height, containerBounds.Width, containerBounds.Height);
                        break;

                    case AnimationType.SlideFromTop:
                        oldView.Frame = new CGRect(0, -containerBounds.Height, containerBounds.Width, containerBounds.Height);
                        break;

                    case AnimationType.ParallaxSlideFromRight:
                        oldView.Frame = new CGRect(containerBounds.Width, 0, containerBounds.Width, containerBounds.Height);
                        newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                        break;

                    case AnimationType.ParallaxSlideFromLeft:
                        oldView.Frame = new CGRect(-containerBounds.Width, 0, containerBounds.Width, containerBounds.Height);
                        newView.Frame = new CGRect(0, 0, containerBounds.Width, containerBounds.Height);
                        break;

                    case AnimationType.Fade:
                        oldView.Alpha = 0;
                        newView.Alpha = 1;
                        break;

                    case AnimationType.ZoomIn:
                        oldView.Alpha = 0;
                        oldView.Transform = CGAffineTransform.MakeScale(0.3f, 0.3f);
                        break;

                    case AnimationType.ZoomOut:
                        oldView.Alpha = 0;
                        oldView.Transform = CGAffineTransform.MakeScale(1.5f, 1.5f);
                        break;

                    case AnimationType.WhirlIn:
                        oldView.Alpha = 0;
                        var whirlOutTransform = CGAffineTransform.MakeRotation((float)System.Math.PI);
                        whirlOutTransform = CGAffineTransform.Scale(whirlOutTransform, 0.3f, 0.3f);
                        oldView.Transform = whirlOutTransform;
                        break;

                    case AnimationType.WhirlIn3:
                        oldView.Alpha = 0;
                        var whirl3OutTransform = CGAffineTransform.MakeRotation((float)(System.Math.PI * 6));
                        whirl3OutTransform = CGAffineTransform.Scale(whirl3OutTransform, 0.3f, 0.3f);
                        oldView.Transform = whirl3OutTransform;
                        break;

                    case AnimationType.None:
                        break;
                }
            };
        }
    }
}
#endif
