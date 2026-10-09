using System.Windows;
using System.Windows.Media;

namespace RssReader.App;

internal static class VisualTreeSearch
{
    internal static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
        {
            return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
