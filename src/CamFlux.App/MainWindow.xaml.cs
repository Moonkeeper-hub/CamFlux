using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace CamFlux.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel model = new();
    private bool closing;
    private bool closed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = model;
        model.PublishingPasswordLoaded += password => Publishing.Password = password;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await model.InitializeAsync();

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (closed) return;
        args.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        await model.CloseAsync();
        closed = true;
        Close();
    }

    private void OnPublishingChanged(object sender, RoutedEventArgs args) =>
        model.PublishPassword = ((PasswordBox)sender).Password;

    private void OnReaderChanged(object sender, RoutedEventArgs args) =>
        model.ReaderPassword = ((PasswordBox)sender).Password;

    private void OnReaderUserChanged(object sender, TextChangedEventArgs args) =>
        model.ReaderUser = ((TextBox)sender).Text;

    private void OnLogChanged(object sender, TextChangedEventArgs args) => ((TextBox)sender).ScrollToEnd();
}
