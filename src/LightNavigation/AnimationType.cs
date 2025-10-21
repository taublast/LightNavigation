namespace LightNavigation;

/// <summary>
/// Built-in page transition animations.
/// </summary>
public enum AnimationType
{
    /// <summary>
    /// Animation corresponding to platform default
    /// </summary>
    Default,

    /// <summary>
    /// No animation - instant transition.
    /// </summary>
    None,

    /// <summary>
    /// New page slides in from the right (standard iOS/Android push).
    /// </summary>
    SlideFromRight,

    /// <summary>
    /// New page slides in from the left.
    /// </summary>
    SlideFromLeft,

    /// <summary>
    /// New page slides up from the bottom (modal style).
    /// </summary>
    SlideFromBottom,

    /// <summary>
    /// New page slides down from the top.
    /// </summary>
    SlideFromTop,

    /// <summary>
    /// New page slides in from right while old page slides left (iOS parallax effect).
    /// </summary>
    ParallaxSlideFromRight,

    /// <summary>
    /// New page slides in from left while old page slides right (reverse parallax).
    /// </summary>
    ParallaxSlideFromLeft,

    /// <summary>
    /// Crossfade between pages.
    /// </summary>
    Fade,

    /// <summary>
    /// New page zooms in from center.
    /// </summary>
    ZoomIn,

    /// <summary>
    /// Current page zooms out to center.
    /// </summary>
    ZoomOut
}