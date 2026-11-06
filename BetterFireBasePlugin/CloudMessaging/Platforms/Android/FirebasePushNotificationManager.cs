using Android.Content;
using Android.App;
using Android.Content;
using Android.Gms.Common;
using Android.Gms.Extensions;
using AndroidX.Core.App;
using Firebase;
using Firebase.Messaging;
using System.Runtime.CompilerServices;
using Application = Android.App.Application;

namespace Plugin.BetterFirebasePushNotification;

public sealed class FirebasePushNotificationManager : DisposableBase, IFirebaseCloudMessaging
{
    private static Context _context;
    private static IList<NotificationUserCategory> userNotificationCategories = new List<NotificationUserCategory>();

    public static string IntentKeyFCMNotification { get; set; } = "intent_key_fcm_notification";
    public static Type NotificationActivityType { get; set; }
    public static string ActionIdentifierKey = "action_identifier";
    public static string ChannelId { get; set; }
    public static int SmallIconRef { private get; set; } = Android.Resource.Drawable.SymDefAppIcon;
    public static Func<FCMNotification, NotificationCompat.Builder> NotificationBuilderProvider { private get; set; }
    public static ActivityFlags? NotificationActivityFlags { get; set; } = ActivityFlags.ClearTop | ActivityFlags.SingleTop;
#nullable enable
    public static Action<FCMNotification>? ShowLocalNotificationAction { private get; set; }
#nullable restore
    private FCMNotification _missedTappedNotification;
    private FCMNotification _missedActionNotification;

    public FirebasePushNotificationManager()
    {
        _context = Application.Context;
    }

    public async Task CheckIfValidAsync()
    {
        var resultCode = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(_context);
        if(resultCode == ConnectionResult.Success) {
            await OnTokenRefreshAsync();
        } else {
            Error?.Invoke(this, new FCMErrorEventArgs(GetErrorMessage(resultCode)));
        }
    }

    private static string GetErrorMessage(int resultCode)
    {
        return GoogleApiAvailability.Instance.IsUserResolvableError(resultCode)
            ? GoogleApiAvailability.Instance.GetErrorString(resultCode)
            : "This device is not supported";
    }

    public async Task OnTokenRefreshAsync()
    {
        var token = await GetTokenAsync();
        TokenChanged?.Invoke(this, new FCMTokenChangedEventArgs(token));
        _onTokenRefresh?.Invoke(this, new FirebasePushNotificationTokenEventArgs(token));
    }

    public void RaiseNotificationReceived(FCMNotification fcmNotification)
    {
        NotificationReceived?.Invoke(this, new FCMNotificationReceivedEventArgs(fcmNotification));
        _onNotificationReceived?.Invoke(this, new FirebasePushNotificationDataEventArgs(fcmNotification.DataAsObjects));
        TryHandleShowLocalNotificationIfNeeded(fcmNotification);
    }

    private static void TryHandleShowLocalNotificationIfNeeded(FCMNotification fcmNotification)
    {
        try {
            HandleShowLocalNotificationIfNeeded(fcmNotification);
        } catch(Exception e) {
            Console.WriteLine($"[Plugin.Firebase]: Couldn't show local push notification: {e.Message}");
        }
    }

    private static void HandleShowLocalNotificationIfNeeded(FCMNotification fcmNotification)
    {
        // Silent (data key "silent") and foreground-silent (is_silent_in_foreground)
        // notifications generate no local notification; the NotificationReceived
        // event still fires.
        if(!fcmNotification.IsSilentInForeground && !fcmNotification.IsSilent) {
            var showLocalNotificationAction = ShowLocalNotificationAction;
            if(showLocalNotificationAction == null) {
                HandleShowLocalNotification(fcmNotification);
            } else {
                showLocalNotificationAction(fcmNotification);
            }
        }
    }
    public static void ClearUserNotificationCategories()
    {
        userNotificationCategories.Clear();
    }

    public static void RegisterUserNotificationCategories(NotificationUserCategory[] notificationCategories)
    {
        if(notificationCategories != null && notificationCategories.Length > 0) {
            ClearUserNotificationCategories();

            foreach(var userCat in notificationCategories) {
                userNotificationCategories.Add(userCat);
            }

        } else {
            ClearUserNotificationCategories();
        }
    }
    public static NotificationUserCategory[] GetUserNotificationCategories()
    {
        return userNotificationCategories?.ToArray();
    }

    /// <summary>
    /// Compat met de oude plugin: lange berichten tonen als big text style.
    /// </summary>
    public static bool UseBigTextStyle { get; set; } = false;

    /// <summary>
    /// Compat overload matching the legacy BetterFirebasePushNotification API.
    /// <paramref name="resetToken"/> en <paramref name="createDefaultNotificationChannel"/>
    /// worden geaccepteerd voor signature-compatibiliteit: de nieuwe implementatie
    /// beheert de token via FirebaseMessaging en gebruikt het notificatiekanaal dat
    /// via <see cref="ChannelId"/> is geconfigureerd.
    /// </summary>
    public static void Initialize(
        NotificationUserCategory[] notificationCategories,
        bool resetToken,
        bool createDefaultNotificationChannel = true,
        bool autoRegistration = true
    )
    {
        RegisterUserNotificationCategories(notificationCategories);
        FirebaseMessaging.Instance.AutoInitEnabled = autoRegistration;
    }

    private static void HandleShowLocalNotification(FCMNotification notification)
    {
        
       
        var intent = _context.PackageManager.GetLaunchIntentForPackage(_context.PackageName);
        intent.PutExtra(IntentKeyFCMNotification, notification.ToBundle());
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

        var pendingIntent = PendingIntent.GetActivity(_context, 0, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = NotificationBuilderProvider?.Invoke(notification) ?? CreateDefaultNotificationBuilder(notification);

        //Check for category
        if(notification.ActionKey != "") {
            var extras = new Bundle();
            var notificationCategories = GetUserNotificationCategories();
            if(notificationCategories != null && notificationCategories.Length > 0) {
                //Check for actions
                foreach(var userCat in notificationCategories) 
                    {
                    if(userCat != null && userCat.Actions != null && userCat.Actions.Count > 0) {
                        foreach(var action in userCat.Actions) 
                            {
                            var aRequestCode = Guid.NewGuid().GetHashCode();
                            if(userCat.Category.Equals(notification.ActionKey, StringComparison.CurrentCultureIgnoreCase)) {
                                Intent actionIntent = null;
                                PendingIntent pendingActionIntent = null;
                                

                                if(action.Type == NotificationActionType.Foreground) {
                                    actionIntent = new Intent(Application.Context, NotificationActivityType);

                                    if(NotificationActivityFlags != null) {
                                        actionIntent.SetFlags(NotificationActivityFlags.Value);
                                    }

                                    extras.PutString(ActionIdentifierKey, action.Id);
                                    // ook de notificatie-payload meegeven zodat OnNewIntent
                                    // de data kan uitlezen en kan forwarden naar
                                    // NotificationActionTapped
                                    actionIntent.PutExtras(extras);
                                    actionIntent.PutExtra(IntentKeyFCMNotification, notification.ToBundle());
                                    pendingActionIntent = PendingIntent.GetActivity(_context, aRequestCode, actionIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                                }
                                if(pendingActionIntent != null) {
                                    builder.AddAction(new NotificationCompat.Action.Builder(_context.Resources.GetIdentifier(action.Icon, "drawable", Application.Context.PackageName), action.Title, pendingActionIntent).Build());
                                }
                            }

                        }
                    }
                }
            }
        }

        
        

        var notificationManager = (NotificationManager) _context.GetSystemService(Context.NotificationService);
        notificationManager.Notify(1337, builder.SetContentIntent(pendingIntent).Build());

    }

    private static NotificationCompat.Builder CreateDefaultNotificationBuilder(FCMNotification notification)
    {
        var builder = new NotificationCompat.Builder(_context, ChannelId)
            .SetSmallIcon(SmallIconRef)
            .TrySetBigPictureStyle(notification)
            .SetContentTitle(notification.Title)
            .SetContentText(notification.Body)
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetAutoCancel(true);

        // Compat met de oude plugin: big text style voor lange berichten
        if(UseBigTextStyle && !string.IsNullOrEmpty(notification.Body)) {
            builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(notification.Body));
        }
        return builder;
    }

    public static void OnNewIntent(Activity activity, Intent intent)
    {
        NotificationActivityType = activity.GetType();

        if(intent == null) {
            return;
        }

        // Tik op een actieknop: extras bevatten ActionIdentifierKey plus de
        // notificatie-payload; deliveren via NotificationActionTapped.
        if(intent.HasExtra(ActionIdentifierKey)) {
            ((FirebasePushNotificationManager) BetterFirebasePushNotification.Current).HandleNotificationActionFromIntent(intent);
            return;
        }

        if(intent.IsNotificationTappedIntent(IntentKeyFCMNotification)) {
            ((FirebasePushNotificationManager) BetterFirebasePushNotification.Current).HandleNotificationFromIntent(intent);
        }
    }
        
    

    private void HandleNotificationFromIntent(Intent intent)
    {
        var notification = intent.GetNotificationFromExtras(IntentKeyFCMNotification);
        if(_notificationTapped == null) {
            _missedTappedNotification = notification;
        } else {
            _notificationTapped.Invoke(this, new FCMNotificationTappedEventArgs(notification));
        }
        if(notification != null) {
            _onNotificationOpened?.Invoke(this, new FirebasePushNotificationResponseEventArgs(notification.DataAsObjects));
        }
        intent.RemoveExtra(IntentKeyFCMNotification);
    }
    private void HandleNotificationActionFromIntent(Intent intent)
    {
        var notification = intent.GetNotificationFromExtras(IntentKeyFCMNotification);
        if(notification != null) {
            var identifier = intent.GetStringExtra(ActionIdentifierKey);
            // identifier in Data zodat de app hem kan uitlezen; click_action geeft
            // compat met de oude plugin mee wélke actie is aangeklikt
            var data = notification.Data == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(notification.Data);
            data[ActionIdentifierKey] = identifier ?? string.Empty;
            data["click_action"] = identifier ?? string.Empty;
            var withIdentifier = new FCMNotification(notification.Body, notification.Title, notification.ImageUrl, data) {
                ChannelId = notification.ChannelId
            };

            if(_notificationActionTapped == null) {
                _missedActionNotification = withIdentifier;
            } else {
                _notificationActionTapped.Invoke(this, new FCMNotificationActionEventArgs(withIdentifier));
            }
            _onNotificationAction?.Invoke(this, new FirebasePushNotificationResponseEventArgs(
                withIdentifier.DataAsObjects,
                identifier ?? string.Empty,
                NotificationCategoryType.Custom));
        }
        intent.RemoveExtra(IntentKeyFCMNotification);
        intent.RemoveExtra(ActionIdentifierKey);
    }

    /// <summary>
    /// Legacy compat (oude plugin): intent-verwerking vanuit MainActivity.OnNewIntent.
    /// </summary>
    public static void ProcessIntent(Activity activity, Intent intent, bool registerActivity = true)
    {
        OnNewIntent(activity, intent);
    }

    /// <summary>
    /// Legacy compat (oude plugin): small icon resource van de notificatie.
    /// </summary>
    public static int IconResource
    {
        get => SmallIconRef;
        set => SmallIconRef = value;
    }

    /// <summary>
    /// Legacy compat (oude plugin): id van het notificatiekanaal; het kanaal
    /// wordt bij het zetten (indien nodig) aangemaakt zodat het meteen bruikbaar is.
    /// </summary>
    public static string DefaultNotificationChannelId
    {
        get => ChannelId;
        set {
            ChannelId = value;
            EnsureNotificationChannel(value);
        }
    }

    private static void EnsureNotificationChannel(string channelId)
    {
        if(Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.N || string.IsNullOrEmpty(channelId)) {
            return;
        }
        var notificationManager = (NotificationManager) _context.GetSystemService(Context.NotificationService);
        if(notificationManager.GetNotificationChannel(channelId) != null) {
            return;
        }
        var channel = new NotificationChannel(channelId, channelId, NotificationImportance.High);
        notificationManager.CreateNotificationChannel(channel);
    }

    public async Task<string> GetTokenAsync()
    {
        // Legacy gedrag: null zolang de FCM-token nog niet is uitgegeven
        // (de oude plugin gooide hier geen exception).
        var token = (await FirebaseMessaging.Instance.GetToken()).ToString();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>
    /// Legacy compat: de huidige FCM-token (null zolang nog niet uitgegeven).
    /// </summary>
    public string Token
    {
        get
        {
            try {
                var token = FirebaseMessaging.Instance.GetToken().Result?.ToString();
                return string.IsNullOrEmpty(token) ? null : token;
            } catch(Exception) {
                return null;
            }
        }
    }

    /// <summary>
    /// Legacy compat: registreren voor push notificaties. Android/FCM doet dit
    /// automatisch; hier wordt de token-eventualiteit alleen vroegtijdig afgevuurd.
    /// </summary>
    public void RegisterForPushNotifications()
    {
        _ = ((FirebasePushNotificationManager) BetterFirebasePushNotification.Current).OnTokenRefreshAsync();
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

    public Task SubscribeToTopicAsync(string topic)
    {
        return FirebaseMessaging.Instance.SubscribeToTopic(topic).AsAsync();
    }

    public Task UnsubscribeFromTopicAsync(string topic)
    {
        return FirebaseMessaging.Instance.UnsubscribeFromTopic(topic).AsAsync();
    }

    public event EventHandler<FCMTokenChangedEventArgs> TokenChanged;
    public event EventHandler<FCMNotificationReceivedEventArgs> NotificationReceived;
    public event EventHandler<FCMErrorEventArgs> Error;

    private event EventHandler<FCMNotificationActionEventArgs> _notificationActionTapped;
    /// <summary>
    /// Gets invoked when the user taps an action button of a notification
    /// (see <see cref="RegisterUserNotificationCategories"/>). The tapped action's
    /// identifier is available in <see cref="FCMNotification.Data"/> under the
    /// <c>ActionIdentifierKey</c> ("action_identifier") key.
    /// </summary>
    public event EventHandler<FCMNotificationActionEventArgs> NotificationActionTapped {
        add {
            _notificationActionTapped += value;
            if(_missedActionNotification != null) {
                _notificationActionTapped?.Invoke(this, new FCMNotificationActionEventArgs(_missedActionNotification));
                _missedActionNotification = null;
            }
        }
        remove => _notificationActionTapped -= value;
    }

    private event EventHandler<FCMNotificationTappedEventArgs> _notificationTapped;
    public event EventHandler<FCMNotificationTappedEventArgs> NotificationTapped {
        add {
            _notificationTapped += value;
            if(_missedTappedNotification != null) {
                _notificationTapped?.Invoke(this, new FCMNotificationTappedEventArgs(_missedTappedNotification));
                _missedTappedNotification = null;
            }
        }
        remove => _notificationTapped -= value;
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