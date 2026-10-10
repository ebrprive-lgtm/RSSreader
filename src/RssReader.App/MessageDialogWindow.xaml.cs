using System.Windows;
using System.Windows.Media;

namespace RssReader.App;

public partial class MessageDialogWindow : Window
{
    private readonly MessageBoxButton _buttons;

    private MessageDialogWindow(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        if (buttons is not MessageBoxButton.OK and not MessageBoxButton.YesNo)
        {
            throw new ArgumentOutOfRangeException(nameof(buttons), buttons, "Only OK and Yes/No dialogs are supported.");
        }

        InitializeComponent();
        _buttons = buttons;
        MessageText.Text = message;
        SeverityMarker.Background = image switch
        {
            MessageBoxImage.Error => (Brush)FindResource("DestructiveBrush"),
            MessageBoxImage.Warning => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("AccentBrush")
        };

        if (buttons == MessageBoxButton.YesNo)
        {
            NoButton.Visibility = Visibility.Visible;
            AcceptButton.Content = "Yes";
            Result = MessageBoxResult.No;
        }
        else
        {
            Result = MessageBoxResult.OK;
        }
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    internal static MessageBoxResult Show(
        Window? owner,
        string message,
        MessageBoxButton buttons,
        MessageBoxImage image,
        Point? ownerRelativeAnchor = null)
    {
        var dialog = new MessageDialogWindow(message, buttons, image);
        if (owner is not null)
        {
            dialog.Owner = owner;
            if (ownerRelativeAnchor is { } anchor)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Opacity = 0;
                dialog.Loaded += (_, _) =>
                {
                    var minimumLeft = owner.Left + 12;
                    var maximumLeft = Math.Max(
                        minimumLeft,
                        owner.Left + owner.ActualWidth - dialog.ActualWidth - 12);
                    var minimumTop = owner.Top + 12;
                    var maximumTop = Math.Max(
                        minimumTop,
                        owner.Top + owner.ActualHeight - dialog.ActualHeight - 12);
                    dialog.Left = Math.Clamp(owner.Left + anchor.X, minimumLeft, maximumLeft);
                    dialog.Top = Math.Clamp(
                        owner.Top + anchor.Y - dialog.ActualHeight / 2,
                        minimumTop,
                        maximumTop);
                    dialog.Opacity = 1;
                };
            }
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        Close();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        Close();
    }
}