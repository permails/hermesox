using Avalonia.Controls;
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

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.BesideScrollRequested += force => _autoScroll?.RequestScrollToBottom(force);
            }
        };
    }
}
