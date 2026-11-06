
namespace Plugin.BetterFirebasePushNotification
{
    /// <summary>
    /// Firebase Messaging lets you reliably deliver messages at no cost. To send or receive messages, the app must get a registration token
    /// from InstanceID. This token authorizes an app server to send messages to an app instance.
    /// </summary>
    public interface IFirebaseCloudMessaging : IDisposable
    {
        /// <summary>
        /// Checks whether device is able to receive cloud messages and if the user has granted the permissions for it.
        /// </summary>
        Task CheckIfValidAsync();

        /// <summary>
        /// Call it when the token was refreshed.
        /// </summary>
        /// <returns></returns>
        Task OnTokenRefreshAsync();

        /// <summary>
        /// Asynchronously gets the default FCM registration token. This creates a Firebase Installations ID, if one does not exist,
        /// and sends information about the application and the device to the Firebase backend. A network connection is required for
        /// the method to succeed.
        /// </summary>
        Task<string> GetTokenAsync();

        /// <summary>
        /// Asynchronously subscribes to a topic. This uses the default FCM registration token to identify the app instance and periodically
        /// sends data to the Firebase backend.
        /// </summary>
        /// <param name="topic">The name of the topic, for example, @“sports”.</param>
        Task SubscribeToTopicAsync(string topic);

        /// <summary>
        /// Asynchronously unsubscribe from a topic. This uses a FCM Token to identify the app instance and periodically sends data to the
        /// Firebase backend.
        /// </summary>
        /// <param name="topic">The name of the topic, for example @“sports”.</param>
        Task UnsubscribeFromTopicAsync(string topic);

        /// <summary>
        /// Call it when a fcm notification was received.
        /// </summary>
        /// <param name="fcmNotification">The received fcm notification.</param>
        void RaiseNotificationReceived(FCMNotification fcmNotification);

        /// <summary>
        /// Legacy compat (oude BetterFirebasePushNotification API): token vernieuwd.
        /// </summary>
        event FirebasePushNotificationTokenEventHandler OnTokenRefresh;

        /// <summary>
        /// Legacy compat (oude BetterFirebasePushNotification API): notificatie ontvangen.
        /// </summary>
        event FirebasePushNotificationDataEventHandler OnNotificationReceived;

        /// <summary>
        /// Legacy compat (oude BetterFirebasePushNotification API): notificatie aange-tikt.
        /// </summary>
        event FirebasePushNotificationResponseEventHandler OnNotificationOpened;

        /// <summary>
        /// Legacy compat (oude BetterFirebasePushNotification API): actieknop aange-tikt.
        /// </summary>
        event FirebasePushNotificationResponseEventHandler OnNotificationAction;

        /// <summary>
        /// Gets invoked when the fcm registration token has changed.
        /// </summary>
        event EventHandler<FCMTokenChangedEventArgs> TokenChanged;

        /// <summary>
        /// Gets invoked when a new fcm notification was received.
        /// </summary>
        event EventHandler<FCMNotificationReceivedEventArgs> NotificationReceived;

        /// <summary>
        /// Gets invoked when a received fcm notification was tapped by the user.
        /// </summary>
        /// <remarks>
        /// On iOS, <see cref="FCMNotification.Data"/> carries the native response's action identifier under the
        /// <c>ActionIdentifier</c> key, including for a tap kept until the first subscriber is added.
        /// </remarks>
        event EventHandler<FCMNotificationTappedEventArgs> NotificationTapped;

        /// <summary>
        /// Gets invoked when the user taps an action button of a notification
        /// (see NotificationUserCategory / RegisterUserNotificationCategories).
        /// </summary>
        /// <remarks>
        /// On iOS the tapped action's identifier is available in <see cref="FCMNotification.Data"/>
        /// under the <c>ActionIdentifier</c> key, on Android under <c>action_identifier</c>.
        /// </remarks>
        event EventHandler<FCMNotificationActionEventArgs> NotificationActionTapped;

        /// <summary>
        /// Gets invoked when something went wrong during the validity check.
        /// </summary>
        event EventHandler<FCMErrorEventArgs> Error;

        /// <summary>
        /// Legacy compat: de huidige FCM-token (null zolang de token nog niet is uitgegeven).
        /// </summary>
        string Token { get; }

        /// <summary>
        /// Legacy compat: toestemming vragen en registreren voor push notificaties.
        /// </summary>
        void RegisterForPushNotifications();

        /// <summary>
        /// Legacy compat: fire-and-forget abonneren op een topic.
        /// </summary>
        void Subscribe(string topic);

        /// <summary>
        /// Legacy compat: fire-and-forget abonnement op een topic opzeggen.
        /// </summary>
        void Unsubscribe(string topic);
    }
}