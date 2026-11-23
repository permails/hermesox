using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HermesDesktop.App.Controls;
using HermesDesktop.App.ViewModels;

namespace HermesDesktop.App.Views;

public partial class BesideChatWindow : Window
{
    private readonly TranscriptAutoScrollController? _autoScroll;

    public BesideChatWindow()
    {
        InitializeComponent();

        if (BesideScroller is not null)
        {
            _autoScroll = new TranscriptAutoScrollController(BesideScroller);
        }

        if (BesideComposerInput is not null)
        {
            BesideComposerInput.AddHandler(InputElement.KeyDownEvent, OnBesideComposerKeyDown, RoutingStrategies.Tunnel);
        }

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.BesideScrollRequested += force => _autoScroll?.RequestScrollToBottom(force);
            }
        };
    }

    private void OnBesideComposerKeyDown(object? sender, KeyEventArgs e)
    {
        var isPlainEnter = e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None;
        var isComboSend = e.Key == Key.Enter
            && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta);

        if ((isPlainEnter || isComboSend)
            && DataContext is MainViewModel vm
            && vm.SendBesideQuickMessageCommand.CanExecute(null))
        {
            e.Handled = true;
            vm.SendBesideQuickMessageCommand.Execute(null);
        }
    }
}
