using Firebase.CloudMessaging;
using Foundation;
using UIKit;
using UserNotifications;

namespace Plugin.BetterFirebasePushNotification;

/// <summary>
/// iOS implementation of <see cref="IFirebaseCloudMessaging"/> that wraps the native Firebase Cloud Messaging SDK.
/// </summary>
[Preserve(AllMembers = true)]
public sealed class FirebasePushNotificationManager
    : NSObject,
        IFirebaseCloudMessaging,
        IUNUserNotificationCenterDelegate,
        IMessagingDelegate
{
    private FCMNotification _missedTappedNotification;
    private readonly NotificationRateLimiter _willPresentRateLimiter = new NotificationRateLimiter();

    private static TimeSpan _rateLimit = new TimeSpan(0, 0, 3);

    /// <summary>
    /// Rate limit (window) voor het onderdrukken van dubbele willPresent-aanroepen
    /// op iOS 18+. Standaard 3 seconden.
    /// </summary>
    public static TimeSpan RateLimit
    {
        get { return _rateLimit; }
        set { _rateLimit = value; }
    }

    /// <summary>
    /// Stelt de rate limit in voor het onderdrukken van dubbele willPresent-aanroepen
    /// op iOS 18+ (compat met de oude plugin).
    /// </summary>
    public static void SetRateLimit(TimeSpan rateLimit)
    {
        RateLimit = rateLimit;
    }
    private FCMNotification _missedActionNotification;

    /// <summary>
    /// Registers notification categories with action buttons (iOS UNNotificationCategory).
    /// Notifications arriving with the aps-category field set to a registered category
    /// display its action buttons; a tap fires <see cref="NotificationActionTapped"/>.
    /// </summary>
    public static void RegisterUserNotificationCategories(NotificationUserCategory[] notificationCategories)
    {
        if(notificationCategories == null || notificationCategories.Length == 0) {
            ClearUserNotificationCategories();
            return;
        }
        ClearUserNotificationCategories();

        var nativeCategories = new List<UNNotificationCategory>();
        foreach(var userCat in notificationCategories) {
            _userNotificationCategories.Add(userCat);
            var actions = new List<UNNotificationAction>();
            foreach(var action in userCat.Actions) {
                UNNotificationActionOptions options;
                switch(action.Type) {
                    case NotificationActionType.AuthenticationRequired:
                        options = UNNotificationActionOptions.AuthenticationRequired;
                        break;
                    case NotificationActionType.Destructive:
                        options = UNNotificationActionOptions.Destructive;
                        break;
                    case NotificationActionType.Foreground:
                        options = UNNotificationActionOptions.Foreground;
                        break;
                    default:
                        options = UNNotificationActionOptions.None;
                        break;
                }
                actions.Add(UNNotificationAction.FromIdentifier(action.Id, action.Title, options));
            }
            var categoryOptions = userCat.Type == NotificationCategoryType.Dismiss
                ? UNNotificationCategoryOptions.CustomDismissAction
                : UNNotificationCategoryOptions.None;
            nativeCategories.Add(UNNotificationCategory.FromIdentifier(
                userCat.Category, actions.ToArray(), new string[] { }, categoryOptions));
        }
        UNUserNotificationCenter.Current.SetNotificationCategories(
            new NSSet<UNNotificationCategory>(nativeCategories.ToArray()));
    }

    /// <summary>
    /// Gets the currently registered notification user categories.
    /// </summary>
    public static NotificationUserCategory[] GetUserNotificationCategories()
    {
        return _userNotificationCategories.ToArray();
    }

    /// <summary>
    /// Removes all registered notification user categories.
    /// </summary>
    public static void ClearUserNotificationCategories()
    {
        _userNotificationCategories.Clear();
    }

    /// <summary>
    /// Forwards a silent (content-available) remote notification to the plugin so it
    /// surfaces as <see cref="NotificationReceived"/>. iOS only delivers
    /// application:didReceiveRemoteNotification:fetchCompletionHandler: on the
    /// UIApplicationDelegate, so call this from your AppDelegate:
    /// <code>
    /// [Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
    /// public void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo, Action&lt;UIBackgroundFetchResult&gt; completionHandler)
    ///     => FirebasePushNotificationManager.DidReceiveRemoteNotification(userInfo, completionHandler);
    /// </code>
    /// </summary>
    public static void DidReceiveRemoteNotification(
        NSDictionary userInfo,
        Action<UIBackgroundFetchResult> completionHandler
    )
    {
        Messaging.SharedInstance.AppDidReceiveMessage(userInfo);
        var instance = (FirebasePushNotificationManager) BetterFirebasePushNotification.Current;
        instance.RaiseNotificationReceived(userInfo.ToSilentFCMNotification());
        completionHandler?.Invoke(UIBackgroundFetchResult.NewData);
    }

    /// <summary>
    /// Legacy compat (oude plugin): APNs-device-token doorsturen vanuit de AppDelegate
    /// ([Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]).
    /// </summary>
    public static void DidRegisterRemoteNotifications(NSData deviceToken)
    {
        Messaging.SharedInstance.ApnsToken = deviceToken;
    }

    /// <summary>
    /// Legacy compat (oude plugin): APNs-registratie mislukt vanuit de AppDelegate
    /// ([Export("application:didFailToRegisterForRemoteNotificationsWithError:")]).
    /// </summary>
    public static void RemoteNotificationRegistrationFailed(NSError error)
    {
        var instance = (FirebasePushNotificationManager) BetterFirebasePushNotification.Current;
        instance.Error?.Invoke(instance, new FCMErrorEventArgs($"APNs registration failed: {error?.LocalizedDescription}"));
    }

    /// <summary>
    /// Legacy compat (oude plugin): silent push doorsturen vanuit de AppDelegate
    /// ([Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]);
    /// volledige variant met completion handler is <see cref="DidReceiveRemoteNotification"/>.
    /// </summary>
    public static void DidReceiveMessage(NSDictionary userInfo)
    {
        Messaging.SharedInstance.AppDidReceiveMessage(userInfo);
        var instance = (FirebasePushNotificationManager) BetterFirebasePushNotification.Current;
        instance.RaiseNotificationReceived(userInfo.ToSilentFCMNotification());
    }

    internal static List<NotificationUserCategory> _userNotificationCategories = new List<NotificationUserCategory>();

    /// <summary>
    /// Initializes the Firebase Cloud Messaging service and registers for remote notifications.
    /// </summary>
    public static void Initialize(bool skipApnsDeviceTokenRequest = false)
    {
        var instance = (FirebasePushNotificationManager) BetterFirebasePushNotification.Current;
        instance.RegisterForRemoteNotifications(skipApnsDeviceTokenRequest);
        if(!skipApnsDeviceTokenRequest) {
            instance.OnTokenRefreshAsync();
        }
    }

    /// <summary>
    /// Compat overload matching the legacy BetterFirebasePushNotification API:
    /// registers the notification user categories and initializes Cloud Messaging.
    /// <paramref name="launchOptions"/> is accepted for signature compatibility
    /// and ignored; <paramref name="autoRegistration"/> = false skips the
    /// APNs device token request.
    /// </summary>
    public static void Initialize(
        NSDictionary launchOptions,
        NotificationUserCategory[] notificationCategories,
        bool autoRegistration = true
    )
    {
        RegisterUserNotificationCategories(notificationCategories);
        Initialize(skipApnsDeviceTokenRequest: !autoRegistration);
    }

    private void RegisterForRemoteNotifications(bool skipApnsDeviceTokenRequest)
    {
        if(UIDevice.CurrentDevice.CheckSystemVersion(10, 0)) {
            UNUserNotificationCenter.Current.Delegate = this;
            Messaging.SharedInstance.Delegate = this;
            if(!skipApnsDeviceTokenRequest) {
                UIApplication.SharedApplication.RegisterForRemoteNotifications();
            }
        } else {
            var allNotificationTypes =
                UIUserNotificationType.Alert
                | UIUserNotificationType.Badge
                | UIUserNotificationType.Sound;
            var settings = UIUserNotificationSettings.GetSettingsForTypes(
                allNotificationTypes,
                null
            );
            UIApplication.SharedApplication.RegisterUserNotificationSettings(settings);
        }
    }

    public static void RequestApnsDeviceToken()
    {
        UIApplication.SharedApplication.RegisterForRemoteNotifications();
        var instance = (FirebasePushNotificationManager) BetterFirebasePushNotification.Current;
        instance.OnTokenRefreshAsync();
    }

    /// <summary>
    /// Triggers a token refresh check and raises the <see cref="TokenChanged"/> event if a token is available.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task OnTokenRefreshAsync()
    {
        RaiseTokenChanged(Messaging.SharedInstance.FcmToken);
        return Task.CompletedTask;
    }

    private void RaiseTokenChanged(string fcmToken)
    {
        TokenChanged?.Invoke(this, new FCMTokenChangedEventArgs(fcmToken));
        _onTokenRefresh?.Invoke(this, new FirebasePushNotificationTokenEventArgs(fcmToken));
    }

    /// <inheritdoc/>
    public Task CheckIfValidAsync()
    {
        if(UIDevice.CurrentDevice.CheckSystemVersion(10, 0)) {
            RequestAuthorization();
        }
        return Task.CompletedTask;
    }

    private void RequestAuthorization()
    {
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert
                | UNAuthorizationOptions.Badge
                | UNAuthorizationOptions.Sound,
            (granted, _) => {
                if(!granted) {
                    Error?.Invoke(
                        this,
                        new FCMErrorEventArgs(
                            "User permission for remote notifications is not granted"
                        )
                    );
                }
            }
        );
    }

    /// <summary>
    /// Handles notification presentation when the app is in the foreground.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="NotificationReceived"/>, then calls <paramref name="completionHandler"/> exactly once.
    /// The presentation options are chosen after the subscribers return, so a subscriber can still set
    /// <c>is_silent_in_foreground</c> in <see cref="FCMNotification.Data"/>. Check <see cref="FCMNotification.Data"/>
    /// for <see langword="null"/> first: it is null when the payload has no alert or <c>notification</c> dictionary,
    /// for example for local notifications, and such a notification can't be marked silent this way.
    /// A silent notification completes with <see cref="UNNotificationPresentationOptions.None"/>; any other
    /// notification completes with <see cref="UNNotificationPresentationOptions.Banner"/>,
    /// <see cref="UNNotificationPresentationOptions.List"/> and <see cref="UNNotificationPresentationOptions.Sound"/>.
    /// If a subscriber or the conversion throws, the completion handler is still called with these options, so the
    /// notification is shown unless it was marked silent before the throw. The exception then propagates out of
    /// this method.
    /// </remarks>
    /// <param name="center">The notification center.</param>
    /// <param name="notification">The notification being presented.</param>
    /// <param name="completionHandler">The completion handler to call with presentation options.</param>
    [Export("userNotificationCenter:willPresentNotification:withCompletionHandler:")]
    public void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler
    )
    {
        // iOS 18 vuurt willPresent soms dubbel voor dezelfde notificatie; de
        // tweede keer binnen de rate limit onderdrukken. In tegenstelling tot
        // de oude plugin wordt de completionHandler hier wél aangeroepen zodat
        // het delegate-contract (exact één keer completen) gerespecteerd blijft.
        if(OperatingSystem.IsIOSVersionAtLeast(18)
            && _willPresentRateLimiter.HasReachedLimit(notification.Request.Identifier, _rateLimit)) {
            completionHandler(UNNotificationPresentationOptions.None);
            return;
        }
        CompleteForegroundNotification(() => notification.ToFCMNotification(), completionHandler);
    }

    internal void CompleteForegroundNotification(
        Func<FCMNotification> getNotification,
        Action<UNNotificationPresentationOptions> completionHandler
    )
    {
        FCMNotification fcmNotification = null;
        try {
            fcmNotification = getNotification();
            RaiseNotificationReceived(fcmNotification);
        }
        finally {
            // the delegate contract requires completing exactly once on every path; reading the
            // options here lets subscribers mark the notification silent before it is presented
            completionHandler(GetPresentationOptions(fcmNotification));
        }
    }

    private static UNNotificationPresentationOptions GetPresentationOptions(
        FCMNotification notification
    )
    {
        return notification?.IsSilentInForeground == true
            ? UNNotificationPresentationOptions.None
            : UNNotificationPresentationOptions.Banner
                | UNNotificationPresentationOptions.List
                | UNNotificationPresentationOptions.Sound;
    }

    /// <summary>
    /// Raises the <see cref="NotificationReceived"/> event with the specified notification.
    /// </summary>
    /// <param name="message">The received FCM notification.</param>
    public void RaiseNotificationReceived(FCMNotification message)
    {
        NotificationReceived?.Invoke(this, new FCMNotificationReceivedEventArgs(message));
        _onNotificationReceived?.Invoke(this, new FirebasePushNotificationDataEventArgs(message.DataAsObjects));
    }

    /// <summary>
    /// Handles the user's response to a notification (e.g., tapping on it).
    /// </summary>
    /// <remarks>
    /// Raises <see cref="NotificationTapped"/>, or keeps the notification until the first subscriber is added,
    /// then calls <paramref name="completionHandler"/> exactly once. If a subscriber throws, the completion
    /// handler is still called and the exception then propagates out of this method.
    /// On both paths, <see cref="FCMNotification.Data"/> carries the response's
    /// <see cref="UNNotificationResponse.ActionIdentifier"/> under the <c>ActionIdentifier</c> key, replacing a
    /// sender-supplied value with that key. A notification without <see cref="FCMNotification.Data"/> is raised
    /// as a copy whose data holds only that entry.
    /// </remarks>
    /// <param name="center">The notification center.</param>
    /// <param name="response">The user's response to the notification.</param>
    /// <param name="completionHandler">The completion handler to call when processing is complete.</param>
    [Export("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:")]
    public void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler
    )
    {
        CompleteNotificationResponse(
            () => response.Notification.ToFCMNotification(),
            () => response.ActionIdentifier?.ToString(),
            completionHandler
        );
    }

    internal void CompleteNotificationResponse(
        Func<FCMNotification> getNotification,
        Func<string> getActionIdentifier,
        Action completionHandler
    )
    {
        try {
            // enrich with the action before choosing immediate or deferred delivery, so a
            // response arriving before any subscriber keeps its ActionIdentifier too
            var notification = WithActionIdentifier(getNotification(), getActionIdentifier());

            var identifier = getActionIdentifier() ?? string.Empty;
            var isDefaultTap = string.IsNullOrEmpty(identifier)
                || identifier.Equals("com.apple.UNNotificationDefaultActionIdentifier", StringComparison.CurrentCultureIgnoreCase);
            var isDismiss = identifier.Equals("com.apple.UNNotificationDismissActionIdentifier", StringComparison.CurrentCultureIgnoreCase);

            if(!isDefaultTap && !isDismiss) {
                // The user tapped one of the registered action buttons:
                // deliver via NotificationActionTapped instead of NotificationTapped.
                // Compat: click_action geeft mee welke actie is aangeklikt.
                if(notification.Data != null) {
                    notification.Data["click_action"] = identifier;
                }
                if(_notificationActionTapped == null) {
                    _missedActionNotification = notification;
                } else {
                    _notificationActionTapped.Invoke(this, new FCMNotificationActionEventArgs(notification));
                }
                _onNotificationAction?.Invoke(this, new FirebasePushNotificationResponseEventArgs(
                    notification.DataAsObjects,
                    GetActionIdentifier(notification),
                    NotificationCategoryType.Custom));
            } else {
                if(_notificationTapped == null) {
                    _missedTappedNotification = notification;
                } else {
                    _notificationTapped.Invoke(this, new FCMNotificationTappedEventArgs(notification));
                }
                _onNotificationOpened?.Invoke(this, new FirebasePushNotificationResponseEventArgs(notification.DataAsObjects));
            }
        }
        finally {
            // the delegate contract requires completing exactly once, even if a subscriber throws
            completionHandler();
        }
    }

    private static FCMNotification WithActionIdentifier(
        FCMNotification notification,
        string actionIdentifier
    )
    {
        if(string.IsNullOrEmpty(actionIdentifier)) {
            return notification;
        }

        const string key = nameof(UNNotificationResponse.ActionIdentifier);
        if(notification.Data == null) {
            // userInfo whose aps dictionary has no alert, or that has neither an aps nor a notification
            // dictionary (such as a local notification), converts to FCMNotification.Empty(), whose Data is
            // null and has no setter; copy it with a dictionary so the action is not dropped
            return new FCMNotification(
                notification.Body,
                notification.Title,
                notification.ImageUrl,
                new Dictionary<string, string> { { key, actionIdentifier } }
            ) { ChannelId = notification.ChannelId };
        }

        notification.Data[key] = actionIdentifier;
        return notification;
    }

    /// <summary>
    /// Called when a new FCM registration token is received.
    /// </summary>
    /// <param name="messaging">The messaging instance.</param>
    /// <param name="fcmToken">The new FCM token.</param>
    [Export("messaging:didReceiveRegistrationToken:")]
    public void DidReceiveRegistrationToken(Messaging messaging, string fcmToken)
    {
        RaiseTokenChanged(fcmToken);
    }

    /// <inheritdoc/>
    public Task<string> GetTokenAsync()
    {
        // Legacy gedrag: null zolang de FCM-token nog niet is uitgegeven
        // (de oude plugin gooide hier geen exception).
        return Task.FromResult(Messaging.SharedInstance.FcmToken);
    }

    /// <summary>
    /// Legacy compat: de huidige FCM-token (null zolang nog niet uitgegeven).
    /// </summary>
    public string Token => Messaging.SharedInstance.FcmToken;

    /// <summary>
    /// Legacy compat: toestemming vragen en registreren voor push notificaties.
    /// </summary>
    public void RegisterForPushNotifications()
    {
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound,
            (granted, error) => {
                if(granted) {
                    UIApplication.SharedApplication.RegisterForRemoteNotifications();
                }
            }
        );
    }

    /// <summary>
    /// Legacy compat: fire-and-forget abonneren op een topic.
    /// </summary>
    public void Subscribe(string topic)
    {
        _ = SubscribeToTopicAsync(topic);
    }

    /// <summary>
    /// Legacy compat: fire-and-forget abonnement op een topic opzeggen.
    /// </summary>
    public void Unsubscribe(string topic)
    {
        _ = UnsubscribeFromTopicAsync(topic);
    }

    /// <inheritdoc/>
    public Task SubscribeToTopicAsync(string topic)
    {
        return Messaging.SharedInstance.SubscribeAsync(topic);
    }

    /// <inheritdoc/>
    public Task UnsubscribeFromTopicAsync(string topic)
    {
        return Messaging.SharedInstance.UnsubscribeAsync(topic);
    }

    /// <inheritdoc/>
    public event EventHandler<FCMTokenChangedEventArgs> TokenChanged;

    /// <inheritdoc/>
    public event EventHandler<FCMNotificationReceivedEventArgs> NotificationReceived;

    /// <inheritdoc/>
    public event EventHandler<FCMErrorEventArgs> Error;

    private event EventHandler<FCMNotificationTappedEventArgs> _notificationTapped;

    /// <inheritdoc/>
    public event EventHandler<FCMNotificationTappedEventArgs> NotificationTapped {
        add {
            _notificationTapped += value;
            if(_missedTappedNotification != null) {
                _notificationTapped?.Invoke(
                    this,
                    new FCMNotificationTappedEventArgs(_missedTappedNotification)
                );
                _missedTappedNotification = null;
            }
        }
        remove => _notificationTapped -= value;
    }

    private event EventHandler<FCMNotificationActionEventArgs> _notificationActionTapped;

    /// <summary>
    /// Gets invoked when the user taps an action button of a notification
    /// (see <see cref="RegisterUserNotificationCategories"/>).
    /// The tapped action's identifier is available in
    /// <see cref="FCMNotification.Data"/> under the <c>ActionIdentifier</c> key.
    /// </summary>
    public event EventHandler<FCMNotificationActionEventArgs> NotificationActionTapped {
        add {
            _notificationActionTapped += value;
            if(_missedActionNotification != null) {
                _notificationActionTapped?.Invoke(
                    this,
                    new FCMNotificationActionEventArgs(_missedActionNotification)
                );
                _missedActionNotification = null;
            }
        }
        remove => _notificationActionTapped -= value;
    }

    private static string GetActionIdentifier(FCMNotification notification)
    {
        return notification.Data != null && notification.Data.ContainsKey("ActionIdentifier")
            ? notification.Data["ActionIdentifier"]
            : string.Empty;
    }

    // Legacy events (oude BetterFirebasePushNotification API): dezelfde momenten
    // als de nieuwe events, met de oude payload-vormen.
    private event FirebasePushNotificationTokenEventHandler _onTokenRefresh;
    /// <inheritdoc/>
    public event FirebasePushNotificationTokenEventHandler OnTokenRefresh {
        add => _onTokenRefresh += value;
        remove => _onTokenRefresh -= value;
    }

    private event FirebasePushNotificationDataEventHandler _onNotificationReceived;
    /// <inheritdoc/>
    public event FirebasePushNotificationDataEventHandler OnNotificationReceived {
        add => _onNotificationReceived += value;
        remove => _onNotificationReceived -= value;
    }

    private event FirebasePushNotificationResponseEventHandler _onNotificationOpened;
    /// <inheritdoc/>
    public event FirebasePushNotificationResponseEventHandler OnNotificationOpened {
        add => _onNotificationOpened += value;
        remove => _onNotificationOpened -= value;
    }

    private event FirebasePushNotificationResponseEventHandler _onNotificationAction;
    /// <inheritdoc/>
    public event FirebasePushNotificationResponseEventHandler OnNotificationAction {
        add => _onNotificationAction += value;
        remove => _onNotificationAction -= value;
    }
}