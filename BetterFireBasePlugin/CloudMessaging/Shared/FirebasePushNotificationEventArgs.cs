using System.Collections.Generic;

namespace Plugin.BetterFirebasePushNotification
{
    /// <summary>
    /// Legacy event types (vorm en naamgeving van de oude BetterFirebasePushNotification-plugin).
    /// De nieuwe fork-events (TokenChanged, NotificationReceived, NotificationTapped,
    /// NotificationActionTapped) vuuren dezelfde momenten; deze legacy-varianten bestaan
    /// zodat bestaande app-code ongewijzigd meegaat in de migratie.
    /// </summary>
    public delegate void FirebasePushNotificationTokenEventHandler(object source, FirebasePushNotificationTokenEventArgs e);

    public delegate void FirebasePushNotificationDataEventHandler(object source, FirebasePushNotificationDataEventArgs e);

    public delegate void FirebasePushNotificationResponseEventHandler(object source, FirebasePushNotificationResponseEventArgs e);

    public class FirebasePushNotificationTokenEventArgs : System.EventArgs
    {
        public string Token { get; }

        public FirebasePushNotificationTokenEventArgs(string token)
        {
            Token = token;
        }
    }

    public class FirebasePushNotificationDataEventArgs : System.EventArgs
    {
        public IDictionary<string, object> Data { get; }

        public FirebasePushNotificationDataEventArgs(IDictionary<string, object> data)
        {
            Data = data;
        }
    }

    public class FirebasePushNotificationResponseEventArgs : System.EventArgs
    {
        public string Identifier { get; }

        public IDictionary<string, object> Data { get; }

        public NotificationCategoryType Type { get; }

        public FirebasePushNotificationResponseEventArgs(
            IDictionary<string, object> data,
            string identifier = "",
            NotificationCategoryType type = NotificationCategoryType.Default
        )
        {
            Identifier = identifier;
            Data = data;
            Type = type;
        }
    }
}
