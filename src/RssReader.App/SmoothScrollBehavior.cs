using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RssReader.App;

public static class SmoothScrollBehavior
{
    private const double WheelDelta = 48;
    private const double AccelerationWindowMilliseconds = 140;
    private const double MaximumAccelerationFactor = 2;
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly ConditionalWeakTable<ScrollViewer, AnimationState> Animations = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SmoothScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    internal static double InterpolateOffset(double startOffset, double targetOffset, double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 1);
        var easedProgress = 1 - Math.Pow(1 - clampedProgress, 3);
        return startOffset + (targetOffset - startOffset) * easedProgress;
    }

    internal static double GetWheelAcceleration(
        double elapsedMilliseconds,
        int previousDirection,
        int currentDirection)
    {
        if (previousDirection == 0 || previousDirection != currentDirection ||
            elapsedMilliseconds >= AccelerationWindowMilliseconds)
        {
            return 1;
        }

        var acceleration = 1 - Math.Clamp(elapsedMilliseconds / AccelerationWindowMilliseconds, 0, 1);
        return 1 + acceleration * (MaximumAccelerationFactor - 1);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if ((bool)eventArgs.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
            element.Unloaded += OnElementUnloaded;
        }
        else
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            element.Unloaded -= OnElementUnloaded;
            StopAnimation(FindScrollViewer(element));
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (sender is not FrameworkElement element || FindScrollViewer(element) is not { } scrollViewer)
        {
            return;
        }

        var state = Animations.GetValue(scrollViewer, _ => new AnimationState());
        var now = Stopwatch.GetTimestamp();
        var direction = Math.Sign(eventArgs.Delta);
        var elapsedMilliseconds = state.LastWheelTimestamp == 0
            ? double.PositiveInfinity
            : Stopwatch.GetElapsedTime(state.LastWheelTimestamp, now).TotalMilliseconds;
        var acceleration = GetWheelAcceleration(
            elapsedMilliseconds,
            state.LastWheelDirection,
            direction);
        var isAnimating = state.RenderingHandler is not null;
        var currentOffset = isAnimating
            ? InterpolateOffset(state.StartOffset, state.TargetOffset, GetProgress(state.StartedAt, now))
            : scrollViewer.VerticalOffset;
        var previousTarget = isAnimating ? state.TargetOffset : scrollViewer.VerticalOffset;
        var targetOffset = Math.Clamp(
            previousTarget - eventArgs.Delta / 120d * WheelDelta * acceleration,
            0,
            scrollViewer.ScrollableHeight);
        if (targetOffset == previousTarget)
        {
            return;
        }

        eventArgs.Handled = true;
        state.StartOffset = currentOffset;
        state.TargetOffset = targetOffset;
        state.StartedAt = now;
        state.LastWheelTimestamp = now;
        state.LastWheelDirection = direction;
        if (state.RenderingHandler is null)
        {
            state.RenderingHandler = (_, _) => OnRendering(scrollViewer, state);
            CompositionTarget.Rendering += state.RenderingHandler;
        }
    }

    private static void OnRendering(ScrollViewer scrollViewer, AnimationState state)
    {
        if (!scrollViewer.IsLoaded)
        {
            StopAnimation(scrollViewer);
            return;
        }

        var progress = GetProgress(state.StartedAt, Stopwatch.GetTimestamp());
        scrollViewer.ScrollToVerticalOffset(
            InterpolateOffset(state.StartOffset, state.TargetOffset, progress));
        if (progress >= 1)
        {
            scrollViewer.ScrollToVerticalOffset(state.TargetOffset);
            StopAnimation(scrollViewer);
        }
    }

    private static double GetProgress(long startedAt, long currentTimestamp) =>
        (currentTimestamp - startedAt) / (double)Stopwatch.Frequency / AnimationDuration.TotalSeconds;

    private static void OnElementUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is FrameworkElement element)
        {
            StopAnimation(FindScrollViewer(element));
        }
    }

    private static void StopAnimation(ScrollViewer? scrollViewer)
    {
        if (scrollViewer is null || !Animations.TryGetValue(scrollViewer, out var state))
        {
            return;
        }

        if (state.RenderingHandler is { } renderingHandler)
        {
            CompositionTarget.Rendering -= renderingHandler;
            state.RenderingHandler = null;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(parent); childIndex++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, childIndex)) is { } childScrollViewer)
            {
                return childScrollViewer;
            }
        }

        return null;
    }

    private sealed class AnimationState
    {
        public double StartOffset { get; set; }
        public double TargetOffset { get; set; }
        public long StartedAt { get; set; }
        public long LastWheelTimestamp { get; set; }
        public int LastWheelDirection { get; set; }
        public EventHandler? RenderingHandler { get; set; }
    }
}