using BaconBackend.DataObjects;
using BaconBackend.Helpers;
using Baconit.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace Baconit.Panels
{
    public sealed partial class UserProfile : UserControl, IPanel
    {
        User m_user;
        IPanelHost m_host;

        public UserProfile()
        {
            this.InitializeComponent();
        }

        public async void PanelSetup(IPanelHost host, Dictionary<string, object> arguments)
        {
            m_host = host;        

            if(!arguments.ContainsKey(PanelManager.NAV_ARGS_USER_NAME))
            {
                ReportUserLoadFailed();
                return;
            }

            // Get the user
            string userName = (string)arguments[PanelManager.NAV_ARGS_USER_NAME];
            ui_userName.Text = userName;

            // Show loading
            ui_loadingOverlay.Show(true, "Finding "+ userName);

            // Make the request
            m_user = await MiscellaneousHelper.GetRedditUser(App.BaconMan, userName);

            // Check we got it.
            if(m_user == null)
            {
                ReportUserLoadFailed();
                return;
            }

            //// Fill in the UI
            //DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            //DateTime postTime = origin.AddSeconds(m_user.CreatedUtc).ToLocalTime();
            //ui_userCreated.Text = $"Redditor for {TimeToTextHelper.TimeElapseToText(postTime)}";

            //// Set cake day
            //TimeSpan elapsed = DateTime.Now - postTime;
            //double fullYears = Math.Floor((elapsed.TotalDays / 365));
            //int daysUntil = (int)(elapsed.TotalDays - (fullYears * 365));
            //ui_cakeDayText.Text = daysUntil == 0 ? "Today is this user's cake day!" : $"{daysUntil} days until cake day";

            //// Set karma
            //ui_linkKarma.Text = $"{String.Format("{0:N0}", m_user.LinkKarma) } link karma";
            //ui_commentKarma.Text = $"{String.Format("{0:N0}", m_user.CommentKarma) } comment karma";

            // Hide loading
            ui_loadingOverlay.Hide();
        }

        private async void ReportUserLoadFailed()
        {
            // Show a message
            App.BaconMan.MessageMan.ShowMessageSimple("Failed To Load", "Check your Internet connection.");

            bool wentBack = false;
            do
            {
                // Try to go back
                wentBack = m_host.GoBack();

                // Wait for a bit and try again.
                await Task.Delay(100);
            }
            while (wentBack);
        }

        public void OnNavigatingFrom()
        {
        }

        public async void OnNavigatingTo()
        {
            // Set the status bar color and get the size returned. If it is not 0 use that to move the
            // color of the page into the status bar.
            double statusBarHeight = await m_host.SetStatusBar(null, 0);
            ui_contentRoot.Margin = new Thickness(0, -statusBarHeight, 0, 0);
            ui_contentRoot.Padding = new Thickness(0, statusBarHeight, 0, 0);
            ui_loadingOverlay.Margin = new Thickness(0, -statusBarHeight, 0, 0);
        }

        public void OnPanelPulledToTop(Dictionary<string, object> arguments)
        {
            OnNavigatingTo();
        }
    }
}
