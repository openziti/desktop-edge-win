using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZitiBackgroundTask.Ziti.Route;

namespace ZitiBackgroundTask
{
    internal class RouteInformation
    {
        private static ObservableCollection<Intercept> _interceptsObservable = new ObservableCollection<Intercept>();

        public static ObservableCollection<Intercept> Intercepts
        {
            get { return _interceptsObservable; }
        }
    }
}
