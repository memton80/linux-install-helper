using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Services;

public interface IDialogService
{
    /// <summary>Asks a question. For destructive actions the safe button is the default one.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryButton, string closeButton, bool destructive = false);

    Task ShowMessageAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    private readonly ILocalizer _localizer;

    public DialogService(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButton, string closeButton, bool destructive = false)
    {
        var dialog = Create(title, message);
        dialog.PrimaryButtonText = primaryButton;
        dialog.CloseButtonText = closeButton;
        dialog.DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = Create(title, message);
        dialog.CloseButtonText = _localizer.Get("Dialog_Ok");
        dialog.DefaultButton = ContentDialogButton.Close;
        await dialog.ShowAsync();
    }

    private static ContentDialog Create(string title, string message)
    {
        var root = App.MainWindow.RootElement;
        return new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.ActualTheme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                Style = (Style)Application.Current.Resources["DialogTextStyle"],
            },
        };
    }
}
