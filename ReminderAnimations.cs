using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DailyReminder;

internal static class ReminderAnimations
{
    internal static void Start(TextBox title, string style, Color accent, double fontScale = 1)
    {
        title.FontWeight = FontWeights.Bold;
        title.Foreground = Brushes.White;
        title.Effect = new DropShadowEffect
        {
            Color = accent,
            ShadowDepth = 3,
            Direction = 275,
            BlurRadius = 4,
            Opacity = 1
        };
        title.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        var rotation = new RotateTransform(0);
        var movement = new TranslateTransform(0, 0);
        title.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { scale, rotation, movement }
        };

        var fontFrames = style switch
        {
            "jelly" => new[] { (0, 13d), (140, 10d), (380, 26d), (650, 22d), (1050, 24d), (1750, 13d) },
            "surprise" => new[] { (0, 13d), (160, 8d), (340, 31d), (550, 24d), (800, 28d), (1250, 23d), (1900, 13d) },
            "breathe" => new[] { (0, 13d), (800, 25d), (1600, 19d), (2400, 13d) },
            _ => new[] { (0, 13d), (120, 9d), (350, 29d), (600, 21d), (850, 26d), (1200, 23d), (1800, 13d) }
        };
        title.BeginAnimation(Control.FontSizeProperty, CreateTrack(
            fontFrames.Select(frame => (frame.Item1, frame.Item2 * fontScale)).ToArray()));

        if (style == "jelly")
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateTrack(
                new[] { (0, 1d), (180, 0.65), (380, 1.12), (650, 0.9), (950, 1.04), (1750, 1d) }));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateTrack(
                new[] { (0, 1d), (180, 1.1), (380, 0.72), (650, 1.14), (950, 0.96), (1750, 1d) }));
        }
        else if (style == "surprise")
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, CreateTrack(
                new[] { (0, 0d), (200, -3d), (380, 3d), (580, -2d), (800, 1d), (1100, 0d), (1900, 0d) }));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateTrack(
                new[] { (0, 0.75), (300, 1.15), (600, 1d), (1900, 0.75) }));
        }
        else if (style == "bounce")
        {
            movement.BeginAnimation(TranslateTransform.YProperty, CreateTrack(
                new[] { (0, 0d), (130, 5d), (350, -9d), (600, 4d), (850, -4d), (1200, 0d), (1800, 0d) }));
        }
    }

    private static DoubleAnimationUsingKeyFrames CreateTrack((int Milliseconds, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(frames[^1].Milliseconds),
            RepeatBehavior = RepeatBehavior.Forever
        };
        foreach (var frame in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                frame.Value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(frame.Milliseconds)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
        }
        return animation;
    }

    internal static void Stop(TextBox title)
    {
        title.BeginAnimation(Control.FontSizeProperty, null);
        title.ClearValue(Control.FontWeightProperty);
        title.ClearValue(Control.ForegroundProperty);
        title.ClearValue(UIElement.EffectProperty);
        title.ClearValue(UIElement.RenderTransformProperty);
    }
}
