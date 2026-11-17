namespace HermesDesktop.Platform.Notifications;

public interface INotificationService
{
    void ShowInfo(string title, string message);
    void ShowNeedsAttention(string sessionTitle, string promptSummary);
}

public sealed class DesktopNotificationService : INotificationService
{
    public event Action<string, string>? NotificationRaised;

    public void ShowInfo(string title, string message)
    {
        NotificationRaised?.Invoke(title, message);
    }

    public void ShowNeedsAttention(string sessionTitle, string promptSummary)
    {
        NotificationRaised?.Invoke($"Needs You — {sessionTitle}", promptSummary);
    }
}
