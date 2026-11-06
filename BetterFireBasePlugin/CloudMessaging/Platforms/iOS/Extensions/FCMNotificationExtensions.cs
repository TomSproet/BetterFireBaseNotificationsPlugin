using UserNotifications;

namespace Plugin.BetterFirebasePushNotification;

/// <summary>
/// Provides extension methods for converting iOS notification types to <see cref="FCMNotification"/>.
/// </summary>
public static class FCMNotificationExtension
{
    /// <summary>
    /// Converts a <see cref="UNNotification"/> to an <see cref="FCMNotification"/>.
    /// </summary>
    /// <param name="notification">The iOS notification to convert.</param>
    /// <returns>An <see cref="FCMNotification"/> containing the notification data.</returns>
    public static FCMNotification ToFCMNotification(this UNNotification notification)
    {
        return notification.Request.ToFCMNotification();
    }

    /// <summary>
    /// Converts a <see cref="UNNotificationRequest"/> to an <see cref="FCMNotification"/>.
    /// </summary>
    /// <param name="request">The iOS notification request to convert.</param>
    /// <returns>An <see cref="FCMNotification"/> containing the notification data.</returns>
    public static FCMNotification ToFCMNotification(this UNNotificationRequest request)
    {
        return request.Content.UserInfo.ToFCMNotification();
    }

    /// <summary>
    /// Converts an <see cref="NSDictionary"/> containing notification user info to an <see cref="FCMNotification"/>.
    /// </summary>
    /// <param name="userInfo">The user info dictionary from the notification.</param>
    /// <returns>An <see cref="FCMNotification"/> containing the notification data, or an empty notification if parsing fails.</returns>
    public static FCMNotification ToFCMNotification(this NSDictionary userInfo)
    {
        if(userInfo["aps"] is NSDictionary apsDict) {
            var alert = apsDict["alert"];
            if(alert is NSDictionary dict) {
                return new FCMNotification(
                    GetBody(dict),
                    GetTitle(dict),
                    GetImageUrl(userInfo),
                    userInfo.ToDictionary()
                );
            } else if(alert != null) {
                return new FCMNotification(alert.ToString(), "", "", userInfo.ToDictionary());
            }
        } else {
            var notification = userInfo["notification"];
            if(notification is NSDictionary dict) {
                return new FCMNotification(
                    GetBody(dict),
                    GetTitle(dict),
                    GetImageUrl(dict),
                    userInfo.ToDictionary()
                );
            }
        }
        return FCMNotification.Empty();
    }

    /// <summary>
    /// Converts a silent (content-available) remote notification's user info into an
    /// <see cref="FCMNotification"/>. Unlike <see cref="ToFCMNotification(System.Collections.Generic.IDictionary{Foundation.NSObject, Foundation.NSObject})"/>,
    /// this keeps the custom data keys of payloads without an alert dictionary: top-level
    /// keys are copied to <see cref="FCMNotification.Data"/> and aps entries flatten to
    /// "aps."-prefixed keys. Use for silent pushes forwarded from AppDelegate.
    /// </summary>
    public static FCMNotification ToSilentFCMNotification(this NSDictionary userInfo)
    {
        var data = new Dictionary<string, string>();
        string title = null;
        string body = null;

        foreach(NSObject key in userInfo.Keys) {
            var keyStr = key.ToString();
            if(keyStr == "aps") {
                if(userInfo[key] is NSDictionary aps) {
                    foreach(NSObject apsKey in aps.Keys) {
                        var apsKeyStr = apsKey.ToString();
                        if(apsKeyStr == "alert") {
                            if(aps[apsKey] is NSDictionary alertDict) {
                                title = GetTitle(alertDict);
                                body = GetBody(alertDict);
                            } else if(aps[apsKey] != null) {
                                body = aps[apsKey].ToString();
                            }
                        } else {
                            data[$"aps.{apsKeyStr}"] = aps[apsKey]?.ToString() ?? string.Empty;
                        }
                    }
                }
            } else {
                data[keyStr] = userInfo[key]?.ToString() ?? string.Empty;
            }
        }
        return new FCMNotification(body, title, null, data);
    }

    private static string GetBody(NSDictionary dict)
    {
        return dict["body"]?.ToString();
    }

    private static string GetTitle(NSDictionary dict)
    {
        return dict["title"]?.ToString();
    }

    private static string GetImageUrl(NSDictionary dict)
    {
        var fcmOptions = dict["fcm_options"] as NSDictionary;
        return fcmOptions?["image"]?.ToString();
    }

    private static Dictionary<string, string> ToDictionary(this NSDictionary dictionary)
    {
        return dictionary.ToDictionary(x => x.Key.ToString(), x => x.Value.ToString());
    }
}