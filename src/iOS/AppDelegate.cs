﻿using System;
using System.Collections.Generic;
using AuthenticationServices;
using Bit.App.Abstractions;
using Bit.App.Resources;
using Bit.App.Services;
using Bit.App.Utilities;
using Bit.Core.Abstractions;
using Bit.Core.Services;
using Bit.Core.Utilities;
using Bit.iOS.Core.Utilities;
using Bit.iOS.Services;
using CoreNFC;
using Foundation;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;

namespace Bit.iOS
{
    [Register("AppDelegate")]
    public partial class AppDelegate : FormsApplicationDelegate
    {
        private NFCNdefReaderSession _nfcSession = null;
        private iOSPushNotificationHandler _pushHandler = null;
        private NFCReaderDelegate _nfcDelegate = null;

        private IDeviceActionService _deviceActionService;
        private IMessagingService _messagingService;
        private IBroadcasterService _broadcasterService;
        private IStorageService _storageService;

        public override bool FinishedLaunching(UIApplication app, NSDictionary options)
        {
            Forms.Init();
            InitApp();
            _deviceActionService = ServiceContainer.Resolve<IDeviceActionService>("deviceActionService");
            _messagingService = ServiceContainer.Resolve<IMessagingService>("messagingService");
            _broadcasterService = ServiceContainer.Resolve<IBroadcasterService>("broadcasterService");
            _storageService = ServiceContainer.Resolve<IStorageService>("storageService");

            LoadApplication(new App.App(null));
            iOSCoreHelpers.AppearanceAdjustments();
            ZXing.Net.Mobile.Forms.iOS.Platform.Init();

            _broadcasterService.Subscribe(nameof(AppDelegate), async (message) =>
            {
                if(message.Command == "scheduleLockTimer")
                {
                    var lockOptionMinutes = (int)message.Data;
                }
                else if(message.Command == "cancelLockTimer")
                {

                }
                else if(message.Command == "updatedTheme")
                {
                    // ThemeManager.SetThemeStyle(message.Data as string);
                }
                else if(message.Command == "copiedToClipboard")
                {

                }
                else if(message.Command == "listenYubiKeyOTP")
                {
                    ListenYubiKey((bool)message.Data);
                }
                else if(message.Command == "showAppExtension")
                {

                }
                else if(message.Command == "showStatusBar")
                {
                    Device.BeginInvokeOnMainThread(() =>
                        UIApplication.SharedApplication.SetStatusBarHidden(!(bool)message.Data, false));
                }
                else if(message.Command == "syncCompleted")
                {
                    if(message.Data is Dictionary<string, object> data && data.ContainsKey("successfully"))
                    {
                        var success = data["successfully"] as bool?;
                        if(success.GetValueOrDefault() && _deviceActionService.SystemMajorVersion() >= 12)
                        {
                            await ASHelpers.ReplaceAllIdentities();
                        }
                    }
                }
                else if(message.Command == "addedCipher" || message.Command == "editedCipher")
                {
                    if(_deviceActionService.SystemMajorVersion() >= 12)
                    {
                        if(await ASHelpers.IdentitiesCanIncremental())
                        {
                            var cipherId = message.Data as string;
                            if(message.Command == "addedCipher" && !string.IsNullOrWhiteSpace(cipherId))
                            {
                                var identity = await ASHelpers.GetCipherIdentityAsync(cipherId);
                                if(identity == null)
                                {
                                    return;
                                }
                                await ASCredentialIdentityStore.SharedStore?.SaveCredentialIdentitiesAsync(
                                    new ASPasswordCredentialIdentity[] { identity });
                                return;
                            }
                        }
                        await ASHelpers.ReplaceAllIdentities();
                    }
                }
                else if(message.Command == "deletedCipher")
                {
                    if(_deviceActionService.SystemMajorVersion() >= 12)
                    {
                        if(await ASHelpers.IdentitiesCanIncremental())
                        {
                            var identity = ASHelpers.ToCredentialIdentity(
                                message.Data as Bit.Core.Models.View.CipherView);
                            if(identity == null)
                            {
                                return;
                            }
                            await ASCredentialIdentityStore.SharedStore?.RemoveCredentialIdentitiesAsync(
                                new ASPasswordCredentialIdentity[] { identity });
                            return;
                        }
                        await ASHelpers.ReplaceAllIdentities();
                    }
                }
                else if(message.Command == "loggedOut")
                {
                    if(_deviceActionService.SystemMajorVersion() >= 12)
                    {
                        await ASCredentialIdentityStore.SharedStore?.RemoveAllCredentialIdentitiesAsync();
                    }
                }
            });

            return base.FinishedLaunching(app, options);
        }

        public override void DidEnterBackground(UIApplication uiApplication)
        {
            var view = new UIView(UIApplication.SharedApplication.KeyWindow.Frame)
            {
                Tag = 4321
            };
            var backgroundView = new UIView(UIApplication.SharedApplication.KeyWindow.Frame)
            {
                BackgroundColor = ((Color)Xamarin.Forms.Application.Current.Resources["SplashBackgroundColor"])
                    .ToUIColor()
            };
            var theme = ThemeManager.GetTheme(false);
            var darkbasedTheme = theme == "dark" || theme == "black" || theme == "nord";
            var logo = new UIImage(darkbasedTheme ? "logo_white.png" : "logo.png");
            var imageView = new UIImageView(logo)
            {
                Center = new CoreGraphics.CGPoint(view.Center.X, view.Center.Y - 30)
            };
            view.AddSubview(backgroundView);
            view.AddSubview(imageView);
            UIApplication.SharedApplication.KeyWindow.AddSubview(view);
            UIApplication.SharedApplication.KeyWindow.BringSubviewToFront(view);
            UIApplication.SharedApplication.KeyWindow.EndEditing(true);
            UIApplication.SharedApplication.SetStatusBarHidden(true, false);
            _storageService.SaveAsync(Bit.Core.Constants.LastActiveKey, DateTime.UtcNow);
            base.DidEnterBackground(uiApplication);
        }

        public override void OnActivated(UIApplication uiApplication)
        {
            base.OnActivated(uiApplication);
            UIApplication.SharedApplication.ApplicationIconBadgeNumber = 0;
            var view = UIApplication.SharedApplication.KeyWindow.ViewWithTag(4321);
            if(view != null)
            {
                view.RemoveFromSuperview();
                UIApplication.SharedApplication.SetStatusBarHidden(false, false);
            }
        }

        public override void WillEnterForeground(UIApplication uiApplication)
        {
            _messagingService.Send("resumed");
            base.WillEnterForeground(uiApplication);
        }

        public override bool OpenUrl(UIApplication application, NSUrl url, string sourceApplication,
            NSObject annotation)
        {
            return true;
        }

        public override void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
        {
            _pushHandler?.OnErrorReceived(error);
        }

        public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
        {
            _pushHandler?.OnRegisteredSuccess(deviceToken);
        }

        public override void DidRegisterUserNotificationSettings(UIApplication application,
            UIUserNotificationSettings notificationSettings)
        {
            application.RegisterForRemoteNotifications();
        }

        public override void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo,
            Action<UIBackgroundFetchResult> completionHandler)
        {
            _pushHandler?.OnMessageReceived(userInfo);
        }

        public override void ReceivedRemoteNotification(UIApplication application, NSDictionary userInfo)
        {
            _pushHandler?.OnMessageReceived(userInfo);
        }

        public void InitApp()
        {
            if(ServiceContainer.RegisteredServices.Count > 0)
            {
                return;
            }

            // Migration services
            ServiceContainer.Register<ILogService>("logService", new ConsoleLogService());
            ServiceContainer.Register("settingsShim", new App.Migration.SettingsShim());
            if(false && App.Migration.MigrationHelpers.NeedsMigration())
            {
                ServiceContainer.Register<App.Migration.Abstractions.IOldSecureStorageService>(
                    "oldSecureStorageService", new Migration.KeyChainStorageService());
            }

            iOSCoreHelpers.RegisterLocalServices();
            RegisterPush();
            ServiceContainer.Init();
            iOSCoreHelpers.RegisterHockeyApp();
            _pushHandler = new iOSPushNotificationHandler(
                ServiceContainer.Resolve<IPushNotificationListenerService>("pushNotificationListenerService"));
            _nfcDelegate = new NFCReaderDelegate((success, message) =>
                _messagingService.Send("gotYubiKeyOTP", message));

            iOSCoreHelpers.Bootstrap();
        }

        private void RegisterPush()
        {
            var notificationListenerService = new PushNotificationListenerService();
            ServiceContainer.Register<IPushNotificationListenerService>(
                "pushNotificationListenerService", notificationListenerService);
            var iosPushNotificationService = new iOSPushNotificationService();
            ServiceContainer.Register<IPushNotificationService>(
                "pushNotificationService", iosPushNotificationService);
        }

        private void ListenYubiKey(bool listen)
        {
            if(_deviceActionService.SupportsNfc())
            {
                _nfcSession?.InvalidateSession();
                _nfcSession?.Dispose();
                _nfcSession = null;
                if(listen)
                {
                    _nfcSession = new NFCNdefReaderSession(_nfcDelegate, null, true)
                    {
                        AlertMessage = AppResources.HoldYubikeyNearTop
                    };
                    _nfcSession.BeginSession();
                }
            }
        }

        private void ShowAppExtension()
        {
            var itemProvider = new NSItemProvider(new NSDictionary(), Core.Constants.UTTypeAppExtensionSetup);
            var extensionItem = new NSExtensionItem();
            extensionItem.Attachments = new NSItemProvider[] { itemProvider };
            var activityViewController = new UIActivityViewController(new NSExtensionItem[] { extensionItem }, null);
            activityViewController.CompletionHandler = (activityType, completed) =>
            {
                // TODO
                //page.EnabledExtension(completed && activityType == "com.8bit.bitwarden.find-login-action-extension");
            };
            var modal = UIApplication.SharedApplication.KeyWindow.RootViewController.ModalViewController;
            if(activityViewController.PopoverPresentationController != null)
            {
                activityViewController.PopoverPresentationController.SourceView = modal.View;
                var frame = UIScreen.MainScreen.Bounds;
                frame.Height /= 2;
                activityViewController.PopoverPresentationController.SourceRect = frame;
            }
            modal.PresentViewController(activityViewController, true, null);
        }
    }
}
