using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.UI.Notifications;
using Windows.UI.ViewManagement;
using Microsoft.Toolkit.Uwp.Notifications; // Notifications library
using Microsoft.QueryStringDotNET;

using Windows.Networking;
using Windows.Networking.Vpn;
using NetFoundry.VPN;

using Newtonsoft.Json;
using NetFoundry.VPN.Util;

// QueryString.NET

namespace TestUWPApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        static string PROFILE_NAME = "NetFoundry VPN Plugin";

        private static readonly Color ColorRequired = new Color() {R = 255};
        private static readonly string _defaultIdentityPath = "Path to identity *";
        private static readonly string _defaultIdentityName = "Path to identity *";
        private static string _keyIdentities = "identites";

        private ApplicationDataContainer localSettings = null;
        private ApplicationDataContainer identities = null;

        public MainPage()
        {
            this.InitializeComponent();
            this.txtIdentityPath.Text = _defaultIdentityPath;
            this.txtIdentityName.Text = _defaultIdentityName;

            localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            identities = localSettings.CreateContainer(_keyIdentities, ApplicationDataCreateDisposition.Always);
            
            ApplicationView.GetForCurrentView().SetPreferredMinSize(new Size(400, 600));
            
            ApplicationView.PreferredLaunchViewSize = new Size(400, 600);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
        }

        private bool isConnected = false;

        // In a real app, these would be initialized with actual data
        string title = "Andrew sent you a picture";
        string content = "Check this out, Happy Canyon in Utah!";
        string image = "https://picsum.photos/360/202?image=883";
        string logo = "ms-appdata:///local/Andrew.jpg";
        private string containerName = "NetFoundry Windows Client";

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string input = "blah";

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            ApplicationDataContainer container =
                localSettings.CreateContainer(containerName, ApplicationDataCreateDisposition.Always);

            if (container.Containers.ContainsKey(_keyIdentities))
            {
                DisplaySimpleToast("Ziti is active!", "Click or tap here to connect one or more identity.",3);
                //                DisplayNoIdentiesToast();
                container.DeleteContainer(_keyIdentities);
            }
            else
            {
                DisplaySimpleToast("Ziti Status - no enrollments", "Ziti has no successfully enrolled identites.", 3);
                //                DisplayNoIdentiesToast();
                ApplicationDataContainer identities =
                    container.CreateContainer(_keyIdentities, ApplicationDataCreateDisposition.Always);
                identities.Values["identity1"] = "this is some json or something";
            }
        }

        private void DisplayNoIdentiesToast()
        {
            // Construct the visuals of the toast
            ToastVisual visual = new ToastVisual()
            {
                BindingGeneric = new ToastBindingGeneric()
                {
                    Children =
                    {
                        new AdaptiveText()
                        {
                            Text = title
                        },

                        new AdaptiveText()
                        {
                            Text = content
                        },

                        new AdaptiveImage()
                        {
                            Source = image
                        }
                    },

                    AppLogoOverride = new ToastGenericAppLogo()
                    {
                        Source = logo,
                        HintCrop = ToastGenericAppLogoCrop.Circle
                    }
                }
            };

            // In a real app, these would be initialized with actual data
            int conversationId = 384928;

            string toastReply = new QueryString()
            {
                {
                    "action", "reply"
                },
                {"conversationId", conversationId.ToString()}

            }.ToString();

            string toastLike = new QueryString()
            {
                {"action", "like"},
                {"conversationId", conversationId.ToString()}

            }.ToString();

            string toastView = new QueryString()
            {
                {"action", "viewImage"},
                {"imageUrl", image}

            }.ToString();

            string toastLaunch = new QueryString()
            {
                {"action", "viewConversation"},
                {"conversationId", conversationId.ToString()}

            }.ToString();
            // Construct the actions for the toast (inputs and buttons)
            ToastActionsCustom actions = new ToastActionsCustom()
            {
                Inputs =
                {
                    new ToastTextBox("tbReply")
                    {
                        PlaceholderContent = "Type a response"
                    }
                },

                Buttons =
                {
                    new ToastButton("Reply", toastReply)
                    {
                        ActivationType = ToastActivationType.Background,
                        ImageUri = "Assets/alert.png",

                        // Reference the text box's ID in order to
                        // place this button next to the text box
                        TextBoxId = "tbReply"
                    },

                    new ToastButton("Like", toastLike)
                    {
                        ActivationType = ToastActivationType.Background
                    },

                    new ToastButton("View", toastView)
                }
            };

            // Now we can construct the final toast content
            ToastContent toastContent = new ToastContent()
            {
                Visual = visual,
                Actions = actions,

                // Arguments when the user taps body of toast
                Launch = toastLaunch
            };

            // And create the toast notification
            var toast = new ToastNotification(toastContent.GetXml());

            toast.ExpirationTime = DateTime.Now.AddMinutes(20);
            toast.Tag = "Connected Identities";
            toast.Group = "NetFoundry Windows Client";


            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }


        private void DisplayPickIdentitesToast()
        {
            // Construct the visuals of the toast
            ToastVisual visual = new ToastVisual()
            {
                BindingGeneric = new ToastBindingGeneric()
                {
                    Children =
                    {
                        new AdaptiveText()
                        {
                            Text = title
                        },

                        new AdaptiveText()
                        {
                            Text = content
                        },

                        new AdaptiveImage()
                        {
                            Source = image
                        }
                    },

                    AppLogoOverride = new ToastGenericAppLogo()
                    {
                        Source = logo,
                        HintCrop = ToastGenericAppLogoCrop.Circle
                    }
                }
            };

            // In a real app, these would be initialized with actual data
            int conversationId = 384928;

            string toastReply = new QueryString()
            {
                {
                    "action", "reply"
                },
                {"conversationId", conversationId.ToString()}

            }.ToString();

            string toastLike = new QueryString()
            {
                {"action", "like"},
                {"conversationId", conversationId.ToString()}

            }.ToString();

            string toastView = new QueryString()
            {
                {"action", "viewImage"},
                {"imageUrl", image}

            }.ToString();

            string toastLaunch = new QueryString()
            {
                {"action", "viewConversation"},
                {"conversationId", conversationId.ToString()}

            }.ToString();


            ToastButton tb = new ToastButton("Reply", toastReply)
            {
                ActivationType = ToastActivationType.Background,
                ImageUri = "Assets/alert.png",

                // Reference the text box's ID in order to
                // place this button next to the text box
                TextBoxId = "tbReply",

            };

            string toastHeaderId = "";
            string toastHeaderTitle = "NetFoundry Toast Header";
            string toastHeaderArguments = "NetFoundry arguments";
            ToastHeader th = new ToastHeader(toastHeaderId, toastHeaderTitle, toastHeaderArguments)
            {
            };

            // Construct the actions for the toast (inputs and buttons)
            ToastActionsCustom actions = new ToastActionsCustom()
            {
                Inputs =
                {
                    new ToastTextBox("tbReply")
                    {
                        PlaceholderContent = "Type a response"
                    }
                },

                Buttons =
                {
                    new ToastButton("Reply", toastReply)
                    {
                        ActivationType = ToastActivationType.Background,
                        ImageUri = "Assets/Reply.png",

                        // Reference the text box's ID in order to
                        // place this button next to the text box
                        TextBoxId = "tbReply"
                    },

                    new ToastButton("Like", toastLike)
                    {
                        ActivationType = ToastActivationType.Background
                    },

                    new ToastButton("View", toastView)
                }
            };

            // Now we can construct the final toast content
            ToastContent toastContent = new ToastContent()
            {
                Visual = visual,
                Actions = actions,

                // Arguments when the user taps body of toast
                Launch = toastLaunch
            };

            // And create the toast notification
            var toast = new ToastNotification(toastContent.GetXml());

            toast.ExpirationTime = DateTime.Now.AddSeconds(2);
            toast.Tag = "18365";
            toast.Group = "wallPosts";


            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }



        private void DisplaySimpleToast(string title, string content, int secondsToDisplay)
        {
            Windows.UI.Notifications.ToastNotificationManager.History.Clear();

            // Construct the visuals of the toast
            ToastVisual visual = new ToastVisual()
            {
                BindingGeneric = new ToastBindingGeneric()
                {
                    Children =
                    {
                        new AdaptiveText()
                        {
                            Text = title
                        },

                        new AdaptiveText()
                        {
                            Text = content
                        } /*,

                        new AdaptiveImage()
                        {
                            Source = image
                        }*/
                    },

                    AppLogoOverride = new ToastGenericAppLogo()
                    {
                        Source = "Assets/ZitiLogo.png", //logo,
                        HintCrop = ToastGenericAppLogoCrop.Circle
                    }
                }
            };

            // Now we can construct the final toast content
            ToastContent toastContent = new ToastContent()
            {
                Visual = visual,
            };

            // And create the toast notification
            var toast = new ToastNotification(toastContent.GetXml());

            toast.ExpirationTime = DateTime.Now.AddSeconds(secondsToDisplay);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            OuterGrid.Height = this.Height;
            OuterGrid.Width = this.Width;
        }

        public async void SaveIdentity(string name, string path)
        {
            identities.Values[name] = path;
        }

        public async void SaveIdentity(Enrollment enrollment)
        {
            // serialize JSON to a string
            string json = JsonConvert.SerializeObject(enrollment);

            // write string to a file
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(enrollment.Name);
            await FileIO.WriteTextAsync(file, json);
        }

        public async Task<string> ReadIdentity(string name)
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(name);
            
            string txt = await FileIO.ReadTextAsync(file);

            return txt;
        }

        private void ImportIdentity_Click(object sender, RoutedEventArgs e)
        {
            byte[] hexBytes = File.ReadAllBytes(@"v:\temp\ip-data2.txt");
            string hexString = Encoding.UTF8.GetString(hexBytes);
            string hexStringa = @"blah";
            byte[] bytes = null;


            bool skip = true;
            if (skip) return;

            string identityName = txtIdentityName.Text;
            string identityPath = txtIdentityName.Text;

            if (string.IsNullOrEmpty(identityName))
            {
                MarkFieldRequired(txtIdentityName, _defaultIdentityName);
            }

            if (string.IsNullOrEmpty(identityPath))
            {
                MarkFieldRequired(txtIdentityPath, _defaultIdentityPath);
            }

            SaveIdentity(identityName, identityPath);
        }

        private void MarkFieldRequired(TextBox txtBox, string text)
        {
            txtBox.Foreground = new SolidColorBrush(ColorRequired);
            txtBox.Text = text;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {

            var (agent, profile) = await GetInstalledVpnProfile();
            if (profile == null)
            {
                // Create a new profile automatically
                var newProfile = new VpnPlugInProfile()
                {
                    AlwaysOn = false,
                    ProfileName = PROFILE_NAME,
                    RequireVpnClientAppUI = false,
                    VpnPluginPackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName,
                    RememberCredentials = false
                };
            }
        }

        private async Task<(VpnManagementAgent Agent, IVpnProfile Profile)> GetInstalledVpnProfile()
        {
            var agent = new VpnManagementAgent();
            var profiles = await agent.GetProfilesAsync();
            var lowerProfileName = PROFILE_NAME.ToLower();
            var profile = profiles.FirstOrDefault(p => p.ProfileName.ToLower() == lowerProfileName);
            return (agent, profile);
        }

        private async void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            VpnManagementAgent mgr = new VpnManagementAgent(); //get the 'vpn manager'
            var profs = await mgr.GetProfilesAsync(); //get the profiles from the local machine

            VpnPlugInProfile nf = getVpnPlugin(profs, PROFILE_NAME); //find our "netfoundry one"
            //nf.ServerUris[0].AbsoluteUri;

            //ziti://192.168.1.31:8900
            if (nf != null)
            {
                VpnManagementErrorStatus deleteStatus = await mgr.DeleteProfileAsync(nf);
                LogHelper.LogLine("result of DELETE: " + deleteStatus);
                if (deleteStatus != VpnManagementErrorStatus.Ok)
                {
                    //do something here maybe
                }
            }

            VpnPlugInProfile pluginProfile = new Windows.Networking.Vpn.VpnPlugInProfile()
            {
                ProfileName = PROFILE_NAME,
                RequireVpnClientAppUI = false,    
                VpnPluginPackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName,
                RememberCredentials = false
            };
            pluginProfile.ServerUris.Add(new Uri("ziti://11.22.33.44:1234"));
            VpnManagementErrorStatus addStatus = await mgr.AddProfileFromObjectAsync(pluginProfile);
            LogHelper.LogLine("result of ADD: " + addStatus);
            if (addStatus != VpnManagementErrorStatus.Ok)
            {
                //do something here maybe
            }
            
        }


        private VpnPlugInProfile getVpnPlugin(IReadOnlyList<IVpnProfile> profs, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Trim();

            foreach (Windows.Networking.Vpn.IVpnProfile prof in profs)
            {
                VpnPlugInProfile p = prof as VpnPlugInProfile;
                LogHelper.LogLine("COUNT: " + p.ServerUris.Count);
                //ZitiBackgroundTask.ZitiVPNPlugin.LogLine(prof.ProfileName);
                if (p != null)
                {
                    if (name == p.ProfileName.Trim())
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
//            ZitiVPNPlugin.SetContext(this);
        }

        private async void Disconnect2_Click(object sender, RoutedEventArgs e)
        {

            VpnManagementAgent mgr = new VpnManagementAgent(); //get the 'vpn manager'
            var profs = await mgr.GetProfilesAsync(); //get the profiles from the local machine

            VpnPlugInProfile nf = getVpnPlugin(profs, PROFILE_NAME); //find our "netfoundry one"

            if (nf != null)
            {
                VpnManagementErrorStatus status = await mgr.DisconnectProfileAsync(nf);
                LogHelper.LogLine("result of DisconnectProfileAsync: " + status);
            }
        }

        private async void Connect2_Click(object sender, RoutedEventArgs e)
        {
            VpnPluginContext vpnContext = VpnPluginContext.GetActiveContext();

            vpnContext.DnsServer = new HostName("192.168.1.114");

            vpnContext.addSuffix("yahoo.com");
            vpnContext.addFQDN("wttr.in");
            vpnContext.addFQDN("eth0.ziti");
            vpnContext.AddIP("5.9.243.187" /*wttr.in*/);
            vpnContext.AddIP("169.254.0.1" /*some FAKE SERVICE that woudl be given to the tunneler*/);
            //vpnContext.AddIP("5.132.162.27" /*eth0.me*/);

            VpnManagementAgent mgr = new VpnManagementAgent(); //get the 'vpn manager'
            var profs = await mgr.GetProfilesAsync(); //get the profiles from the local machine

            VpnPlugInProfile nf = getVpnPlugin(profs, PROFILE_NAME); //find our "netfoundry one"
            if(nf != null)
            {
                VpnManagementErrorStatus status = await mgr.ConnectProfileAsync(nf);
                LogHelper.LogLine("result of connect: " + status);
            }
        }
    }

    public class Enrollment
    {
        public string Name { get; set; }
        public string EnrollmentAsJson { get; set; }
    }
}
