using BaconBackend.Collectors;
using BaconBackend.DataObjects;
using BaconBackend.Helpers;
using Baconit.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace Baconit.Panels
{
    public sealed partial class UserProfile : UserControl, IPanel
    {
        /// <summary>
        /// Holds the user
        /// </summary>
        User m_user;

        /// <summary>
        /// Holds a ref to the host
        /// </summary>
        IPanelHost m_host;

        /// <summary>
        /// Holds the subreddit collector if we have one.
        /// </summary>
        PostCollector m_postCollector = null;

        /// <summary>
        /// Holds the comment collector if we have one.
        /// </summary>
        CommentCollector m_commentCollector = null;

        /// <summary>
        /// Holds the list of current posts
        /// </summary>
        ObservableCollection<Post> m_postList = new ObservableCollection<Post>();

        /// <summary>
        /// Holds the list of current comments
        /// </summary>
        ObservableCollection<Comment> m_commentList = new ObservableCollection<Comment>();


        public UserProfile()
        {
            this.InitializeComponent();

            // Set the list sources
            ui_postList.ItemsSource = m_postList;
            ui_commentList.ItemsSource = m_commentList;
        }

        public void PanelSetup(IPanelHost host, Dictionary<string, object> arguments)
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

            // Do the loading on a thread to get off the UI
            Task.Run(async () =>
            {
                // Make the request
                m_user = await MiscellaneousHelper.GetRedditUser(App.BaconMan, userName);

                // Jump back to the UI thread, we will use low priority so we don't make any animations choppy.
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    // Check we got it.
                    if (m_user == null)
                    {
                        ReportUserLoadFailed();
                        return;
                    }

                    // Fill in the UI
                    DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
                    DateTime postTime = origin.AddSeconds(m_user.CreatedUtc).ToLocalTime();
                    ui_accountAgeText.Text = $"{TimeToTextHelper.TimeElapseToText(postTime)} old";

                    // Set cake day
                    TimeSpan elapsed = DateTime.Now - postTime;
                    double fullYears = Math.Floor((elapsed.TotalDays / 365));
                    int daysUntil = (int)(elapsed.TotalDays - (fullYears * 365));
                    ui_cakeDayHolder.Visibility = daysUntil == 0 ? Visibility.Visible : Visibility.Collapsed;

                    // Set karma
                    ui_linkKarmaText.Text = String.Format("{0:N0}", m_user.LinkKarma);
                    ui_commentKarmaText.Text = String.Format("{0:N0}", m_user.CommentKarma);

                    // Set Gold
                    ui_goldHolder.Visibility = m_user.IsGold ? Visibility.Visible : Visibility.Collapsed;

                    // Hide loading
                    ui_loadingOverlay.Hide();

                    // Kick off the updates
                    GetUserComments();
                    GetUserPosts();
                });
            });
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

        #region Posts

        /// <summary>
        /// Ensures a post collector has been made
        /// </summary>
        public void EnsurePostCollector()
        {
            lock (this)
            {
                if (m_postCollector == null)
                {
                    m_postCollector = PostCollector.GetCollector(m_user, App.BaconMan);
                    m_postCollector.OnCollectionUpdated += PostCollector_OnCollectionUpdated;
                    m_postCollector.OnCollectorStateChange += PostCollector_OnCollectorStateChange;
                }
            }
        }

        /// <summary>
        /// Kicks off a update for users posts
        /// </summary>
        private void GetUserPosts()
        {
            if(m_user == null)
            {
                return;
            }

            EnsurePostCollector();

            // Update the collector
            m_postCollector.Update();
        }

        /// <summary>
        /// Fired when the post list is loading
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PostCollector_OnCollectorStateChange(object sender, OnCollectorStateChangeArgs e)
        {
            // Jump to the UI thread
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                // Kill anything we have
                ui_postLoadingBar.IsIndeterminate = false;
                ui_postLoadingBar.Visibility = Visibility.Collapsed;
                ui_postLoadingRing.IsActive = false;
                ui_postLoadingRing.Visibility = Visibility.Collapsed;

                // Set the new UI
                if (e.State == CollectorState.Extending)
                {
                    ui_postLoadingBar.IsIndeterminate = true;
                    ui_postLoadingBar.Visibility = Visibility.Visible;
                }
                else if (e.State == CollectorState.Updating)
                {
                    ui_postLoadingRing.IsActive = true;
                    ui_postLoadingRing.Visibility = Visibility.Visible;
                }

                // Check for no stories
                if ((e.State == CollectorState.Idle || e.State == CollectorState.FullyExtended) && m_postList.Count == 0 && e.NewPostCount == 0)
                {
                    ui_postNoPostsText.Visibility = Visibility.Visible;
                }
            });
        }

        /// <summary>
        /// Fired when posts are updated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PostCollector_OnCollectionUpdated(object sender, OnCollectionUpdatedArgs<Post> e)
        {
            // Jump to the UI thread
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                // If this is fresh clear
                if(e.IsFreshUpdate)
                {
                    m_postList.Clear();
                }

                // Add the new posts to the end of the list
                foreach (Post p in e.ChangedItems)
                {
                    m_postList.Add(p);
                }
            });
        }

        /// <summary>
        /// Fired when a post is tapped
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PostList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(ui_postList.SelectedIndex == -1)
            {
                return;
            }

            // Get the post
            Post tappedPost = (Post)ui_postList.SelectedItem;

            // Navigate to the post
            Dictionary<string, object> args = new Dictionary<string, object>();
            args.Add(PanelManager.NAV_ARGS_SUBREDDIT_NAME, tappedPost.Subreddit);
            args.Add(PanelManager.NAV_ARGS_FORCE_POST_ID, tappedPost.Id);
            // Make sure the page id is unique
            m_host.Navigate(typeof(FlipViewPanel), tappedPost.Subreddit + SortTypes.Hot + SortTimeTypes.Week + tappedPost.Id, args);

            // Color the title so the user knows they read this.
            tappedPost.TitleTextColor = Color.FromArgb(255, 152, 152, 152);

            // Reset the selected index
            ui_postList.SelectedIndex = -1;
        }
        
        /// <summary>
        /// Fired when the list is scrolled down a ways.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PostList_OnListEndDetectedEvent(object sender, HelperControls.OnListEndDetected e)
        {
            if(m_user == null)
            {
                return;
            }

            EnsurePostCollector();

            // Request a extension.
            m_postCollector.ExtendCollection();
        }

        #endregion

        #region Comments

        /// <summary>
        /// Ensures a post collector has been made
        /// </summary>
        public void EnsureCommentCollector()
        {
            lock (this)
            {
                if (m_commentCollector == null)
                {
                    m_commentCollector = CommentCollector.GetCollector(m_user, App.BaconMan);
                    m_commentCollector.OnCollectionUpdated += CommentCollector_OnCollectionUpdated;
                    m_commentCollector.OnCollectorStateChange += CommentCollector_OnCollectorStateChange;
                }
            }
        }

        /// <summary>
        /// Kicks off a update for users comments
        /// </summary>
        private void GetUserComments()
        {
            if (m_user == null)
            {
                return;
            }

            EnsureCommentCollector();

            // Update the collector
            m_commentCollector.Update();
        }

        /// <summary>
        /// Fired when the comment list is loading
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void CommentCollector_OnCollectorStateChange(object sender, OnCollectorStateChangeArgs e)
        {
            // Jump to the UI thread
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                // Kill anything we have
                ui_commentLoadingBar.IsIndeterminate = false;
                ui_commentLoadingBar.Visibility = Visibility.Collapsed;
                ui_commentLoadingRing.IsActive = false;
                ui_commentLoadingRing.Visibility = Visibility.Collapsed;

                // Set the new UI
                if (e.State == CollectorState.Extending)
                {
                    ui_commentLoadingBar.IsIndeterminate = true;
                    ui_commentLoadingBar.Visibility = Visibility.Visible;
                }
                else if (e.State == CollectorState.Updating)
                {
                    ui_commentLoadingRing.IsActive = true;
                    ui_commentLoadingRing.Visibility = Visibility.Visible;
                }

                // Check for no comments
                if ((e.State == CollectorState.Idle || e.State == CollectorState.FullyExtended) && m_commentList.Count == 0 && e.NewPostCount == 0)
                {
                    ui_postNoPostsText.Visibility = Visibility.Visible;
                }
            });
        }

        /// <summary>
        /// Fired when comments are updated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void CommentCollector_OnCollectionUpdated(object sender, OnCollectionUpdatedArgs<Comment> e)
        {
            // Jump to the UI thread
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                // If this is fresh clear
                if (e.IsFreshUpdate)
                {
                    m_commentList.Clear();
                }

                // Add the new posts to the end of the list
                foreach (Comment comment in e.ChangedItems)
                {
                    m_commentList.Add(comment);
                }
            });
        }

        /// <summary>
        /// Fired when a post is tapped
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CommentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ui_commentList.SelectedIndex == -1)
            {
                return;
            }

            // Get the post
            Comment tappedComment = (Comment)ui_commentList.SelectedItem;

            // Navigate flip view and force it to the post and comment.
            Dictionary<string, object> args = new Dictionary<string, object>();
            args.Add(PanelManager.NAV_ARGS_SUBREDDIT_NAME, tappedComment.Subreddit);
            args.Add(PanelManager.NAV_ARGS_FORCE_POST_ID, tappedComment.LinkId.Substring(3));
            args.Add(PanelManager.NAV_ARGS_FORCE_COMMENT_ID, tappedComment.Id);

            // Make sure the page Id is unique
            m_host.Navigate(typeof(FlipViewPanel), tappedComment.Subreddit + SortTypes.Hot + SortTimeTypes.Week + tappedComment.LinkId + tappedComment.Id, args);

            // Reset the selected index
            ui_commentList.SelectedIndex = -1;
        }

        /// <summary>
        /// Fired when the list is scrolled down a ways.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CommentList_OnListEndDetectedEvent(object sender, HelperControls.OnListEndDetected e)
        {
            if (m_user == null)
            {
                return;
            }

            EnsureCommentCollector();

            // Request a extension.
            m_commentCollector.ExtendCollection();
        }

        /// <summary>
        /// Fired when a link is tapped in the comment list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MarkdownTextBlock_OnMarkdownLinkTapped(object sender, UniversalMarkdown.OnMarkdownLinkTappedArgs e)
        {
            App.BaconMan.ShowGlobalContent(e.Link);
        }

        #endregion
    }
}
