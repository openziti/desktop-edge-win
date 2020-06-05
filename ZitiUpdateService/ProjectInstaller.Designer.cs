namespace ZitiUpdateService {
	partial class ProjectInstaller {
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing) {
			if (disposing && (components != null)) {
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
			this.ZitiUpdateProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
			this.ZitiUpdateServiceInstaller = new System.ServiceProcess.ServiceInstaller();
			// 
			// ZitiUpdateProcessInstaller
			// 
			this.ZitiUpdateProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
			this.ZitiUpdateProcessInstaller.Password = null;
			this.ZitiUpdateProcessInstaller.Username = null;
			// 
			// ZitiUpdateServiceInstaller
			// 
			this.ZitiUpdateServiceInstaller.Description = "Keep Ziti Service Software Up To Date";
			this.ZitiUpdateServiceInstaller.DisplayName = "Ziti Update Service";
			this.ZitiUpdateServiceInstaller.ServiceName = "ZitiUpdateService";
			this.ZitiUpdateServiceInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
			this.ZitiUpdateServiceInstaller.AfterInstall += new System.Configuration.Install.InstallEventHandler(this.ZitiUpdateServiceInstaller_AfterInstall);
			// 
			// ProjectInstaller
			// 
			this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.ZitiUpdateProcessInstaller,
            this.ZitiUpdateServiceInstaller});

		}

		#endregion

		private System.ServiceProcess.ServiceProcessInstaller ZitiUpdateProcessInstaller;
		private System.ServiceProcess.ServiceInstaller ZitiUpdateServiceInstaller;
	}
}