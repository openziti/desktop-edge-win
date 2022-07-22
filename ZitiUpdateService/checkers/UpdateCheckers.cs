using System;
using NLog;

namespace ZitiUpdateService.Checkers {

    abstract class UpdateCheck
	{
		public DateTime PublishDate { get; set; }
		public string FileName { get; set; }
		public int Avail { get; set; }

		internal Version compareTo;

		public UpdateCheck(Version current)
        {
			compareTo = current;
        }

        public abstract void CopyUpdatePackage(string destinationFolder, string destinationName);
        public abstract bool AlreadyDownloaded(string destinationFolder, string destinationName);
        public abstract bool HashIsValid(string destinationFolder, string destinationName);
		public abstract Version GetNextVersion();

	}
}
