#if ANDROID
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Debug = System.Diagnostics.Debug;
using AView = Android.Views.View;

namespace LightNavigation.Platform
{
    /// <summary>
    /// COMPLETELY CUSTOM NavigationPage handler for Android that DOES NOT use fragments.
    /// This eliminates black flashing and navigation issues on Android.
    /// </summary>
    public class LightNavigationViewHandler : ViewHandler<NavigationPage, FrameLayout>
    {
        private const string TAG = "[LightNavigation_Android]";
        private readonly List<Page> _viewStack = new();
        private Page? _currentView;

        // Animation settings
        private const int ANIMATION_IN_DURATION_MS = 150;
        private const int ANIMATION_OUT_DURATION_MS = 100;

        // Navigation operation queue to prevent concurrent operations and ensure smooth transitions
        private readonly Queue<Func<Task>> _navigationQueue = new();
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isProcessingQueue = false;

        public LightNavigationViewHandler() : base(PropertyMapper, CommandMapper)
        {
            Debug.WriteLine($"{TAG} ✅ Handler created");
        }

        /// <summary>
        /// Enqueues a navigation operation and processes the queue sequentially.
        /// This prevents concurrent navigation operations that cause crashes and visual glitches.
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

        public static IPropertyMapper<NavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<NavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
            };

        public static CommandMapper<NavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<NavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        protected override FrameLayout CreatePlatformView()
        {
            // Create a simple FrameLayout container - NO fragments!
            var container = new FrameLayout(Context)
            {
                LayoutParameters = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent)
            };

            // Set background to transparent
            container.SetBackgroundColor(Android.Graphics.Color.Transparent);

            return container;
        }

        protected override void DisconnectHandler(FrameLayout platformView)
        {
            // Clean up views
            platformView.RemoveAllViews();
            _navigationQueue.Clear();
            _viewStack.Clear();
            _currentView = null;
            _navigationSemaphore?.Dispose();

            base.DisconnectHandler(platformView);
        }

        private static void MapRequestNavigation(LightNavigationViewHandler handler, NavigationPage view, object? args)
        {
            if (args is not NavigationRequest request)
            {
                Debug.WriteLine($"{TAG} ⚠️ MapRequestNavigation called with invalid args");
                return;
            }

            handler.HandleNavigationRequest(request);
        }

        private void HandleNavigationRequest(NavigationRequest request)
        {
            _ = EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var newStack = request.NavigationStack;
                    var currentStackCount = _viewStack.Count;

                    Debug.WriteLine($"{TAG} 📊 Current stack: {currentStackCount}, New stack: {newStack.Count}");

                    if (newStack.Count > currentStackCount)
                    {
                        // Push - show the new page
                        var newPage = newStack[newStack.Count - 1] as Page;
                        if (newPage != null)
                        {
                            if (currentStackCount == 0)
                            {
                                await ShowPageAsync(newPage, false, isInitial: true);
                            }
                            else
                            {
                                await ShowPageAsync(newPage, request.Animated, isInitial: false);
                            }
                        }
                    }
                    else if (newStack.Count < currentStackCount)
                    {
                        // Pop
                        var diff = currentStackCount - newStack.Count;

                        // Pop the required number of pages
                        for (int i = 0; i < diff; i++)
                        {
                            await PopPageAsync(request.Animated);
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
                    Debug.WriteLine($"{TAG} ❌ Error handling navigation request: {ex.Message}");
                }
            });
        }

        private Task ShowPageAsync(Page page, bool animate, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                // Convert MAUI page to Android View
                var newView = page.ToPlatform(MauiContext!);

                if (newView == null)
                {
                    Debug.WriteLine($"{TAG} ❌ ToPlatform returned null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} ✅ Created Android view for page");

                var container = PlatformView;
                var oldPage = _currentView;
                var oldView = _currentView?.Handler?.PlatformView as AView;

                var newAware = page as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                newAware?.OnPushing();
                oldAware?.OnPopping();

                if (isInitial || oldView == null)
                {
                    // Set as root - no animation
                    container.AddView(newView);
                    _currentView = page;
                    _viewStack.Add(page);
                    oldAware?.OnRemoved();
                    newAware?.OnTopmost();
                    tcs.SetResult(true);
                }
                else
                {
                    if (animate)
                    {
                        // Set initial animation state
                        newView.Alpha = 0.3f;
                        newView.TranslationX = container.Width * 0.15f; // Start slightly to the right
                        oldView.Visibility = ViewStates.Visible;
                    }

                    container.AddView(newView);

                    _viewStack.Add(page);

                    // Wait for new view to be laid out and rendered
                    newView.Post(() =>
                    {
                        if (animate)
                        {
                            // Animate new view: fade in + slide from right
                            newView.Animate()
                                .Alpha(1f)
                                .TranslationX(0f)
                                .SetDuration(ANIMATION_IN_DURATION_MS)
                                .SetInterpolator(new Android.Views.Animations.DecelerateInterpolator())
                                .WithEndAction(new Java.Lang.Runnable(() =>
                                {
                                    // AFTER animation completes, hide old view
                                    newView.TranslationX = 0;
                                    newView.Alpha = 1;
                                    oldView.Visibility = ViewStates.Invisible;

                                    newAware?.OnTopmost();
                                    oldAware?.OnRemoved();
                                    tcs.SetResult(true);
                                }))
                                .Start();
                        }
                        else
                        {
                            // No animation - hide old view immediately
                            oldView.Visibility = ViewStates.Invisible;

                            newAware?.OnTopmost();
                            oldAware?.OnRemoved();
                            tcs.SetResult(true);
                        }
                    });

                    _currentView = page;
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in ShowPageAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopPageAsync(bool animate)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ⚠️ Cannot pop - only one page on stack");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                var container = PlatformView;

                if (_viewStack.Count > 1)
                {
                    var page = _viewStack[_viewStack.Count - 2];
                    var newView = page.Handler!.PlatformView as AView;
                    var oldPage = _viewStack.Last();
                    var oldView = oldPage.Handler!.PlatformView as AView;

                    var newAware = page as INavigationAware;
                    var oldAware = oldPage as INavigationAware;

                    oldAware?.OnPopping();

                    if (animate && oldView != null && newView != null)
                    {
                        oldView.TranslationX = 0;
                        oldView.Alpha = 1;
                        oldView.Visibility = ViewStates.Visible;
                        // Keep old view on top for animation
                        oldView.BringToFront();

                        newView.TranslationX = 0;
                        newView.Alpha = 1;
                        newView.Visibility = ViewStates.Visible;
                    }

                    Debug.WriteLine($"{TAG} OLDVIEW visibility: {oldView?.Visibility}");

                    newView?.Post(() =>
                    {
                        if (animate && oldView != null)
                        {
                            Debug.WriteLine($"{TAG} 🎬 Starting pop animation");
                            oldView.Visibility = ViewStates.Visible;

                            // Animate old view: slide out to right + fade out
                            oldView.Animate()
                                .Alpha(0f)
                                .TranslationX(container.Width * 0.3f)
                                .SetDuration(ANIMATION_OUT_DURATION_MS)
                                .SetInterpolator(new Android.Views.Animations.AccelerateInterpolator())
                                .WithEndAction(new Java.Lang.Runnable(() =>
                                {
                                    container.RemoveView(oldView);
                                    if (newView != null)
                                    {
                                        newView.Alpha = 1;
                                        newView.TranslationX = 0;
                                    }

                                    // Update stacks AFTER animation completes
                                    _currentView = page;
                                    _viewStack.Remove(oldPage);

                                    oldAware?.OnRemoved();
                                    newAware?.OnTopmost();
                                    Debug.WriteLine($"{TAG} 📊 Stack count after pop: {_viewStack.Count}");
                                    Debug.WriteLine($"{TAG} ✅ Pop animation complete!");
                                    tcs.SetResult(true);
                                }))
                                .Start();
                        }
                        else
                        {
                            // No animation - just remove immediately
                            if (oldView != null)
                            {
                                container.RemoveView(oldView);
                            }

                            // Make sure the new view is visible
                            if (newView != null)
                            {
                                newView.Visibility = ViewStates.Visible;
                                newView.Alpha = 1;
                                newView.TranslationX = 0;
                                newView.BringToFront();
                            }

                            // Update stacks immediately (no animation)
                            _currentView = page;
                            _viewStack.Remove(oldPage);

                            oldAware?.OnRemoved();
                            newAware?.OnTopmost();
                            Debug.WriteLine($"{TAG} 📊 Stack count after pop: {_viewStack.Count}");
                            Debug.WriteLine($"{TAG} ✅ Pop complete");
                            tcs.SetResult(true);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in PopPageAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopToRootAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ℹ️ Already at root");
                    tcs.SetResult(true);
                    return tcs.Task;
                }

                var container = PlatformView;

                // Get the root view (first item in stack)
                var page = _viewStack.First();
                var rootView = page.Handler!.PlatformView as AView;

                Debug.WriteLine($"{TAG} 🏠 PopToRoot - showing root view");

                // Make root view visible
                if (rootView != null)
                {
                    rootView.Visibility = ViewStates.Visible;
                }

                // Remove all other views from container
                var viewsToRemove = _viewStack.Skip(1).ToList();

                rootView?.Post(() =>
                {
                    foreach (var oldPage in viewsToRemove)
                    {
                        var oldView = oldPage.Handler?.PlatformView as AView;
                        if (oldView != null)
                        {
                            container.RemoveView(oldView);
                        }
                        if (oldPage is INavigationAware oldAware)
                        {
                            oldAware.OnRemoved();
                        }
                    }

                    tcs.SetResult(true);
                });

                // Clear stack except root
                _viewStack.Clear();
                _viewStack.Add(page);
                _currentView = page;

                if (page is INavigationAware newAware)
                {
                    newAware.OnTopmost();
                }

                Debug.WriteLine($"{TAG} 📊 Stack count after PopToRoot: {_viewStack.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in PopToRootAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }
    }
}
#endif
