using System.ComponentModel;
using System.Configuration.Install;

namespace ZitiUpdateService {
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer {
        public ProjectInstaller() {
            InitializeComponent();
        }

        private void ZitiUpdateServiceInstaller_AfterInstall(object sender, InstallEventArgs e) {

        }
    }
}
