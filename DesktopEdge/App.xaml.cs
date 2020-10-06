using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace ZitiDesktopEdge {
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App:Application {
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e) {

            const string appName = "Ziti Desktop Edge";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew) {
                Application.Current.Shutdown();
            }

            base.OnStartup(e);
        }
    }
}
