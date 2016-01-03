﻿using BaconBackend.DataObjects;
using Baconit.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


namespace Baconit.FlipViewControls
{
    public sealed partial class WebPageFlipControl : UserControl, IFlipViewContentControl
    {
        /// <summary>
        /// Reference to the host
        /// </summary>
        IFlipViewContentHost m_host;

        /// <summary>
        /// Holds a reference to the webview.
        /// </summary>
        WebView m_webView;

        /// <summary>
        /// Indicates if we have called hide or not.
        /// </summary>
        bool m_loadingHidden;

        /// <summary>
        /// Indicates if we should be destroyed
        /// </summary>
        bool m_isDestroyed = false;

        /// <summary>
        /// The url of the post we are viewing.
        /// </summary>
        string m_postUrl = String.Empty;

        /// <summary>
        /// Indicate is reading mode is enabled or not.
        /// </summary>
        bool m_isReadigModeEnabled = false;

        public WebPageFlipControl(IFlipViewContentHost host)
        {
            this.InitializeComponent();
            m_host = host;

            // Hide the full screen icon if we can't go full screen.
            ui_fullScreenHolder.Visibility = m_host.CanGoFullScreen ? Visibility.Visible : Visibility.Collapsed;

            // Listen for back presses
            App.BaconMan.OnBackButton += BaconMan_OnBackButton;
        }

        /// <summary>
        /// Called by the host when it queries if we can handle a post.
        /// </summary>
        /// <param name="post"></param>
        /// <returns></returns>
        static public bool CanHandlePost(Post post)
        {
            // Web view is the fall back, we should be handle just about anything.
            return true;
        }

        /// <summary>
        /// Called by the host when we should show content.
        /// </summary>
        /// <param name="post"></param>
        public async void OnPrepareContent(Post post)
        {
            // So the loading UI
            m_host.ShowLoading();
            m_loadingHidden = false;

            // Since some of this can be costly, delay the work load until we aren't animating.
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                lock (this)
                {
                    if (m_isDestroyed)
                    {
                        return;
                    }

                    // Get the post url
                    m_postUrl = post.Url;

                    // Make the webview
                    m_webView = new WebView(WebViewExecutionMode.SeparateThread);

                    // Setup the listeners, we need all of these because some web pages don't trigger
                    // some of them.
                    m_webView.FrameNavigationCompleted += NavigationCompleted;
                    m_webView.NavigationFailed += NavigationFailed;
                    m_webView.DOMContentLoaded += DOMContentLoaded;
                    m_webView.ContentLoading += ContentLoading;
                    m_webView.ContainsFullScreenElementChanged += WebView_ContainsFullScreenElementChanged;

                    // Navigate
                    try
                    {
                        m_webView.Navigate(new Uri(post.Url, UriKind.Absolute));
                    }
                    catch (Exception e)
                    {
                        App.BaconMan.TelemetryMan.ReportUnExpectedEvent(this, "FailedToMakeUriInWebControl", e);
                        m_host.ShowError();
                    }

                    // Now add an event for navigating.
                    m_webView.NavigationStarting += NavigationStarting;

                    // Insert this before the full screen button.
                    ui_contentRoot.Children.Insert(0, m_webView);
                }
            });
        }

        /// <summary>
        /// Called when the  post actually becomes visible
        /// </summary>
        public void OnVisible()
        {
            // Ignore for now
        }

        public void OnDestroyContent()
        {
            lock (this)
            {
                m_isDestroyed = true;

                if (m_webView != null)
                {
                    // Clear handlers
                    m_webView.FrameNavigationCompleted -= NavigationCompleted;
                    m_webView.NavigationFailed -= NavigationFailed;
                    m_webView.DOMContentLoaded -= DOMContentLoaded;
                    m_webView.ContentLoading -= ContentLoading;

                    // Clear the webview
                    m_webView.Stop();
                    m_webView.NavigateToString("");
                }
                m_webView = null;
            }

            // Clear the UI
            ui_contentRoot.Children.Remove(m_webView);
        }


        private void DOMContentLoaded(WebView sender, WebViewDOMContentLoadedEventArgs args)
        {
            HideLoading();
            HideReadingModeLoading();
        }

        private void ContentLoading(WebView sender, WebViewContentLoadingEventArgs args)
        {
            HideLoading();
            HideReadingModeLoading();
        }

        private void NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            HideLoading();
            HideReadingModeLoading();
        }
        private void NavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            m_host.ShowError();
        }

        private void NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            ToggleBackButton(true);
        }

        /// <summary>
        /// Calls hide loading on the host if we haven't already.
        /// </summary>
        private void HideLoading()
        {
            // Make sure we haven't been called before.
            lock (m_host)
            {
                if (m_loadingHidden)
                {
                    return;
                }
                m_loadingHidden = true;
            }

            // Hide it.
            m_host.HideLoading();
        }

        #region Full Screen Logic

        /// <summary>
        /// Fired when the user taps the full screen button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FullScreenHolder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleFullScreen(!m_host.IsFullScreen());
        }

        /// <summary>
        /// Fire when the user presses back.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BaconMan_OnBackButton(object sender, BaconBackend.OnBackButtonArgs e)
        {
            if (e.IsHandled)
            {
                return;
            }

            // Kill it if we are.
            e.IsHandled = ToggleFullScreen(false);
        }

        /// <summary>
        /// Fired when something in the website goes full screen.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void WebView_ContainsFullScreenElementChanged(WebView sender, object args)
        {
            if (m_webView == null)
            {
                return;
            }

            if (m_webView.ContainsFullScreenElement)
            {
                // Go full screen
                ToggleFullScreen(true);

                // Hide the full screen button, let the webcontrol take care of it (we don't want to overlap
                ui_fullScreenHolder.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Jump out of full screen
                ToggleFullScreen(false);

                // Restore the button
                ui_fullScreenHolder.Visibility = m_host.CanGoFullScreen ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private bool ToggleFullScreen(bool goFullScreen)
        {
            // Set the state
            bool didAction = m_host.ToggleFullScreen(goFullScreen);

            // Update the icon
            ui_fullScreenIcon.Symbol = m_host.IsFullScreen() ? Symbol.BackToWindow : Symbol.FullScreen;

            // Set our manipulation mode to capture all input
            ManipulationMode = m_host.IsFullScreen() ? ManipulationModes.All : ManipulationModes.System;

            return didAction;
        }

        #endregion

        private void ReadingMode_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Show loading
            ui_readingModeLoading.Visibility = Visibility.Visible;
            ui_readingModeLoading.IsActive = true;
            ui_readingModeIconHolder.Visibility = Visibility.Collapsed;

            // Flip
            m_isReadigModeEnabled = !m_isReadigModeEnabled;

            // Get the URI
            Uri webLink = null;
            if (m_isReadigModeEnabled)
            {
                webLink = new Uri("http://www.readability.com/m?url=" + m_postUrl, UriKind.Absolute);
            }           
            else
            {
                webLink = new Uri(m_postUrl, UriKind.Absolute);
            }

            // Navigate.
            try
            {
                m_webView.Navigate(webLink);
            }
            catch (Exception ex)
            {
                App.BaconMan.TelemetryMan.ReportUnExpectedEvent(this, "FailedToNavReadingMode", ex);
                m_host.ShowError();
            }

            App.BaconMan.TelemetryMan.ReportEvent(this, "ReadingModeEnabled");
        }

        private void HideReadingModeLoading()
        {
            ui_readingModeLoading.Visibility = Visibility.Collapsed;
            ui_readingModeLoading.IsActive = false;
            ui_readingModeIconHolder.Visibility = Visibility.Visible;
        }

        private async void BackButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (m_webView != null)
            {
                // Go back
                if (m_webView.CanGoBack)
                {
                    m_webView.GoBack();
                }

                // Delay a little while so CanGoBack gets updated
                await Task.Delay(500);

                ToggleBackButton(m_webView.CanGoBack);
            }
        }

        public void ToggleBackButton(bool show)
        {
            if ((show && ui_backButton.Opacity == 1) ||
                (!show && ui_backButton.Opacity == 0))
            {
                return;
            }

            if (show)
            {
                ui_backButton.Visibility = Visibility.Visible;
            }
            anim_backButtonFade.To = show ? 1 : 0;
            anim_backButtonFade.From = show ? 0 : 1;
            story_backButtonFade.Begin();
        }

        private void BackButtonFade_Completed(object sender, object e)
        {
            if (ui_backButton.Opacity == 0)
            {
                ui_backButton.Visibility = Visibility.Collapsed;
            }
        }
    }
}
