namespace Plugin.BetterFirebasePushNotification
{
    public sealed class FCMNotificationActionEventArgs
    {
        public FCMNotificationActionEventArgs(FCMNotification notification)
        {
            Notification = notification;
        }

        public FCMNotification Notification { get; }
    }
}