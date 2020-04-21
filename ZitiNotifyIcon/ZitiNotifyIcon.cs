using System;
using System.Windows.Forms;

namespace ZitiNotifyIcon {
	public class ZitiNotifyIcon:IDisposable {

		public NotifyIcon targetNotifyIcon;
		private System.Drawing.Point notifyIconMousePosition;
		private Timer delayMouseLeaveEventTimer;
		public delegate void MouseLeaveHandler();
		public event MouseLeaveHandler MouseLeave;
		public delegate void MouseMoveHandler();
		public event MouseMoveHandler MouseMove;
		
		public ZitiNotifyIcon(int millisecondsToDelayMouseLeaveEvent) {
			targetNotifyIcon = new NotifyIcon();
			targetNotifyIcon.Visible = true;
			targetNotifyIcon.MouseMove += new MouseEventHandler(targetNotifyIcon_MouseMove);

			delayMouseLeaveEventTimer = new Timer();
			delayMouseLeaveEventTimer.Tick += new EventHandler(delayMouseLeaveEventTimer_Tick);
			delayMouseLeaveEventTimer.Interval = 1000;
		}
		
		public ZitiNotifyIcon() : this(1000) { }
		
		public void StartMouseLeaveTimer() {
			delayMouseLeaveEventTimer.Start();
		}
		
		public void StopMouseLeaveEventFromFiring() {
			delayMouseLeaveEventTimer.Stop();
		}

		public void targetNotifyIcon_MouseMove(object sender, MouseEventArgs e) {
			notifyIconMousePosition = System.Windows.Forms.Control.MousePosition; 
			MouseMove(); 
			delayMouseLeaveEventTimer.Start();  
		}
		
		void delayMouseLeaveEventTimer_Tick(object sender, EventArgs e) {
			if (notifyIconMousePosition != System.Windows.Forms.Control.MousePosition) {
				MouseLeave(); 
				delayMouseLeaveEventTimer.Stop(); 
			}
		}

		#region IDisposable Members
		
		private bool _IsDisposed = false;

		~ZitiNotifyIcon() {
			Dispose(false);
		}

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(true);
		}

		protected virtual void Dispose(bool IsDisposing) {
			if (_IsDisposed) return;
			if (IsDisposing) targetNotifyIcon.Dispose();
			_IsDisposed = true;
			#endregion
		}
	}
}
