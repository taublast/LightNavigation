using Microsoft.Maui.Controls;

namespace LightNavigation
{
    /// <summary>
    /// A high-performance NavigationPage implementation that eliminates black flashing
    /// during page transitions on all platforms.
    ///
    /// Features:
    /// - Smooth animations on Android, iOS, and Windows
    /// - No black flash during navigation transitions
    /// - Support for INavigationAware lifecycle callbacks
    /// - Queue-based navigation to prevent concurrent operations
    ///
    /// Usage:
    /// Replace your NavigationPage with LightNavigationPage:
    /// <code>
    /// MainPage = new LightNavigationPage(new YourRootPage());
    /// </code>
    /// </summary>
    public class LightNavigationPage : NavigationPage
    {
        private static AnimationType _defaultTransition = AnimationType.Default;

        /// <summary>
        /// Sets the default transition animation type to use when a page doesn't have a specific transition set.
        /// This allows you to override the platform default behavior globally.
        /// </summary>
        /// <param name="transition">The transition type to use as default. Cannot be AnimationType.Default (use the platform default instead).</param>
        public static void SetDefaultTransition(AnimationType transition)
        {
            _defaultTransition = transition;
        }

        /// <summary>
        /// Gets the current default transition animation type.
        /// </summary>
        public static AnimationType GetDefaultTransition()
        {
            return _defaultTransition;
        }

        /// <summary>
        /// Resolves the effective transition for a page, taking into account:
        /// 1. Page-specific transition (if set)
        /// 2. Global default transition (if set via SetDefaultTransition)
        /// 3. Platform default (AnimationType.Default)
        /// </summary>
        public static AnimationType GetEffectiveTransition(BindableObject target)
        {
            var pageTransition = (AnimationType)target.GetValue(TransitionProperty);

            // If page has a specific transition set (not Default), use it
            if (pageTransition != AnimationType.Default)
            {
                return pageTransition;
            }

            // Otherwise, use the global default transition
            return _defaultTransition;
        }

        /// <summary>
        /// Attached property for specifying the transition animation type for a page.
        /// Usage in XAML: ln:LightNavigationPage.Transition="Fade"
        /// </summary>
        public static readonly BindableProperty TransitionProperty = BindableProperty.CreateAttached(
            propertyName: "Transition",
            returnType: typeof(AnimationType),
            declaringType: typeof(LightNavigationPage),
            defaultValue: AnimationType.Default,
            defaultBindingMode: BindingMode.OneWay);

        /// <summary>
        /// Gets the transition animation type for the specified page.
        /// This is the getter for the attached property.
        /// </summary>
        public static AnimationType GetTransition(BindableObject target)
        {
            return (AnimationType)target.GetValue(TransitionProperty);
        }

        /// <summary>
        /// Sets the transition animation type for the specified page.
        /// This is the setter for the attached property.
        /// </summary>
        public static void SetTransition(BindableObject target, AnimationType value)
        {
            target.SetValue(TransitionProperty, value);
        }

        /// <summary>
        /// Attached property for specifying custom transition speed (duration in milliseconds).
        /// Set to 0 to use the built-in default speed for the transition.
        /// Usage in XAML: ln:LightNavigationPage.TransitionSpeed="300"
        /// </summary>
        public static readonly BindableProperty TransitionSpeedProperty = BindableProperty.CreateAttached(
            propertyName: "TransitionSpeed",
            returnType: typeof(int),
            declaringType: typeof(LightNavigationPage),
            defaultValue: 0,
            defaultBindingMode: BindingMode.OneWay);

        /// <summary>
        /// Gets the transition speed (duration in ms) for the specified page.
        /// Returns 0 if no custom speed is set (use default).
        /// </summary>
        public static int GetTransitionSpeed(BindableObject target)
        {
            return (int)target.GetValue(TransitionSpeedProperty);
        }

        /// <summary>
        /// Sets the transition speed (duration in ms) for the specified page.
        /// Set to 0 to use the built-in default speed.
        /// </summary>
        public static void SetTransitionSpeed(BindableObject target, int value)
        {
            target.SetValue(TransitionSpeedProperty, value);
        }

        /// <summary>
        /// Attached property for specifying the transition easing/interpolation type.
        /// Set to TransitionEasing.Default (0) to use the built-in default easing.
        /// Usage in XAML: ln:LightNavigationPage.TransitionEasing="Linear"
        /// </summary>
        public static readonly BindableProperty TransitionEasingProperty = BindableProperty.CreateAttached(
            propertyName: "TransitionEasing",
            returnType: typeof(TransitionEasing),
            declaringType: typeof(LightNavigationPage),
            defaultValue: TransitionEasing.Default,
            defaultBindingMode: BindingMode.OneWay);

        /// <summary>
        /// Gets the transition easing type for the specified page.
        /// Returns TransitionEasing.Default if no custom easing is set.
        /// </summary>
        public static TransitionEasing GetTransitionEasing(BindableObject target)
        {
            return (TransitionEasing)target.GetValue(TransitionEasingProperty);
        }

        /// <summary>
        /// Sets the transition easing type for the specified page.
        /// Set to TransitionEasing.Default to use the built-in default easing.
        /// </summary>
        public static void SetTransitionEasing(BindableObject target, TransitionEasing value)
        {
            target.SetValue(TransitionEasingProperty, value);
        }

        /// <summary>
        /// Creates a new instance of LightNavigationPage with the specified root page.
        /// </summary>
        /// <param name="root">The root page to display in the navigation stack.</param>
        public LightNavigationPage(Page root) : base(root)
        {
            this.Popped += OnPopRequested;
            this.PoppedToRoot += OnPopToRootRequested;
        }

        private void OnPopToRootRequested(object? sender, NavigationEventArgs navigationEventArgs)
        {
            try
            {
                this.PoppedToRoot -= OnPopToRootRequested;
                this.Popped -= OnPopRequested;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"[LightNavigationPage] Error in OnPopToRootRequested: {exception}");
            }
        }

        private void OnPopRequested(object? sender, NavigationEventArgs navigationEventArgs)
        {
            try
            {
                this.Popped -= OnPopRequested;
                this.PoppedToRoot -= OnPopToRootRequested;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"[LightNavigationPage] Error in OnPopRequested: {exception}");
            }
        }
    }
}
