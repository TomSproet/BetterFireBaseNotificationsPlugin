using Firebase.Messaging;

namespace Plugin.BetterFirebasePushNotification;

[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class MyFirebaseMessagingService : FirebaseMessagingService
{
    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);
        
        BetterFirebasePushNotification.Current.RaiseNotificationReceived(message.ToFCMNotification());
    }

    public override async void OnNewToken(string token)
    {
        await BetterFirebasePushNotification.Current.OnTokenRefreshAsync();
    }
}