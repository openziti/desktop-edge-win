using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge.Models {
	public class ZitiService {
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		public string Name { get; set; }
		public string[] Protocols { get; set; }
		public Address[] Addresses { get; set; }
		public PortRange[] Ports { get; set; }
		public PostureCheck[] PostureChecks { get; set; }
		public bool OwnsIntercept { get; set; }
		public string AssignedIP { get; set; }
		public DateTime TimeUpdated { get; set; }
		public int Timeout { get; set; }
		public int TimeoutRemaining { get; set; }
		public bool IsMfaReady { get; set; }

		private bool failingPostureCheck;
		public bool HasFailingPostureCheck() {
			return failingPostureCheck;
		}
		public bool IsAccessible { get; set; }

		public string Warning {
			get {
				if (this.OwnsIntercept) {
					return "";
				} else {
					return "this won't trigger right now"; //$"Another identity already mapped the specified hostname: {Host}.\nThis service is only available via IP";
				}
			}
		}

		public ZitiService() {
		}

		public ZitiService(Service svc) {
			this.Name = svc.Name;
			this.AssignedIP = svc.AssignedIP;
			this.Addresses = svc.Addresses;
			this.Protocols = svc.Protocols == null ? null : svc.Protocols.Select(p => p.ToUpper()).ToArray();
			this.Ports = svc.Ports;
			this.PostureChecks = svc.PostureChecks;
			this.Timeout = svc.Timeout;
			this.TimeoutRemaining = svc.TimeoutRemaining;
			this.OwnsIntercept = svc.OwnsIntercept;
			this.IsMfaReady = false;
			this.TimeUpdated = DateTime.Now;
			if (this.PostureChecks != null) {
				this.failingPostureCheck = this.PostureChecks.Any(p => !p.IsPassing);
			}
			this.IsAccessible = svc.IsAccessible;
			//commented out for now logger.Warn("SERVICE: " + this.Name + " HAS FAILING POSTURE CHECK: " + failingPostureCheck);
		}

		public string WarningMessage {
			get {
				string message = Warning;
				if (this.PostureChecks != null && this.PostureChecks.Length > 0) {
					List<MessageCount> messages = new List<MessageCount>();
					for (int i = 0; i < this.PostureChecks.Length; i++) {
						if (!this.PostureChecks[i].IsPassing) {
							messages = AppendMessage(messages, this.PostureChecks[i].QueryType);
						}
					}
					if (messages.Count > 0) {
						string checks = "";
						for (int i = 0; i < messages.Count; i++) {
							checks += ((i > 0) ? ", " : "") + messages[i].Total + " " + messages[i].Message;
						}
						message = message + " Posture Check Failing: " + checks;
						return message.Trim();
					} else return message;
				} else return message;
			}
		}
		private List<MessageCount> AppendMessage(List<MessageCount> items, string message) {
			bool found = false;
			for (int i = 0; i < items.Count; i++) {
				if (items[i].Message == message) {
					items[i].Total++;
					found = true;
				}
			}
			if (!found) {
				MessageCount count = new MessageCount();
				count.Total = 1;
				count.Message = message;
				items.Add(count);
			}
			return items;
		}

		public int WarnWidth {
			get {
				return (HasWarning) ? 30 : 0;
			}
			set { }
		}

		public Visibility WarningVisibility { 
			get {
				return (HasWarning) ? Visibility.Visible : Visibility.Collapsed;
			}
			set { }
		}
		public int WarningWidth {
			get {
				return (HasWarning) ? 20 : 0;
			}
			set { }
		}

		public Visibility TimerVisibility {
			get {
				return (IsMfaReady && TimeoutCalculated > -1 && TimeoutCalculated <= 1200 && TimeoutCalculated > 0) ? Visibility.Visible : Visibility.Collapsed;
			}
			set { }
		}

		public int TimeoutCalculated { 
			get {
				if (this.TimeoutRemaining == -1 || TimeoutRemaining == 0) return this.TimeoutRemaining;
				else {
					TimeSpan t = (DateTime.Now - this.TimeUpdated.ToLocalTime());
					int timeout = this.TimeoutRemaining - (int)Math.Floor(t.TotalSeconds);
					if (timeout < 0) timeout = 0;
					return timeout;
				}
			}
			set { }
		}

		public Visibility MfaVisibility {
			get {
				return (IsMfaReady && TimeoutCalculated > -1 && TimeoutCalculated == 0) ? Visibility.Visible : Visibility.Collapsed;
			}
			set { }
		}
		public int TimerWidth {
			get {
				return (TimeoutCalculated > -1 && TimeoutCalculated < 1200) ? 20 : 0;
			}
			set { }
		}

		public bool HasWarning {
			get {
				if (!this.OwnsIntercept) return true;
				else {
					if (this.IsAccessible) {
						return false;
					} else {
						if (this.PostureChecks==null) {
							return false;
						} else {
							for (int i = 0; i < this.PostureChecks.Length; i++) {
								if (!this.PostureChecks[i].IsPassing) {
									return true;
								}
							}
							return false;
						}
					}
				}
			}
			set { }
		}

		public string ProtocolString {
			get {
				string toReturn = "";
				for (int i = 0; i < this.Protocols.Length; i++) {
					toReturn += ((i > 0) ? "," : "") + this.Protocols[i];
				}
				return toReturn;
			}
			set { }
		}

		public string PortString {
			get {
				string toReturn = "";
				for (int i = 0; i < this.Ports.Length; i++) {
					toReturn += ((i > 0) ? "," : "") + this.Ports[i].ToString();
				}
				return toReturn;
			}
			set { }
		}

		public string AddressString { 
			get {
				string toReturn = "";
				for (int i = 0; i < this.Addresses.Length; i++) {
					toReturn += ((i > 0) ? "," : "") + this.Addresses[i].ToString();
				}
				return toReturn;
			}
			set { }
		}

		private ServiceMatrix builtMatrix = null;
		public ServiceMatrix Matrix {
			get {
				if (builtMatrix == null) {
					builtMatrix = new ServiceMatrix();
					List<ServiceMatrixElement> matrix = new List<ServiceMatrixElement>(this.Protocols.Length * this.Addresses.Length * this.Ports.Length);

					foreach (var proto in this.Protocols) {
						foreach (var addy in this.Addresses) {
							foreach (var port in this.Ports) {
								ServiceMatrixElement m = new ServiceMatrixElement();
								m.Ports = port.ToString();
								m.Proto = proto.ToUpper();
								m.Address = addy.Hostname;

								matrix.Add(m);
							}
						}
					}
					builtMatrix.Elements = matrix;
				}

				return builtMatrix;
			}
		}

		public override string ToString() {
			string protos = "<none>";
			if (Protocols?.Length > 0) {
				if (Protocols.Length > 1) {
					protos = "[" + string.Join(",", Protocols.Select(p => p.ToString())) + "]";
				} else {
					protos = Protocols[0];
				}
			}
			string addys = "<none>";
			if (Addresses?.Length > 0) {
				if (Addresses.Length > 1) {
					addys = "[" + string.Join(",", Addresses.Select(a => a.ToString()).OrderBy(o => o)) + "]";
				} else {
					addys = Addresses[0].ToString();
				}
			}
			string ranges = "<none>";
			if (Ports?.Length > 0) {
				if (Ports.Length > 1) {
					ranges = "[" + string.Join(",", Ports.Select(a => a.ToString()).OrderBy(o => o)) + "]";
				} else {
					ranges = Ports[0].ToString();
				}
			}

			return protos + ":" + addys + ":" + ranges;
		}
	}

	public class ServiceMatrix {
		public List<ServiceMatrixElement> Elements { get; internal set; }
	}

	public class ServiceMatrixElement {
		public ServiceMatrixElement() {
			Proto = "<none>";
			Address = "<none>";
			Ports = "<none>";
		}

		public string Proto { get; set; }
		public string Address { get; set; }
		public string Ports { get; set; }

		public override string ToString() {
			return Proto + " " + Address + " " + Ports;
		}
	}
}
