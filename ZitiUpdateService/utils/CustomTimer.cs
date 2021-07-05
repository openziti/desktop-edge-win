using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZitiUpdateService.Utils {
    class CustomTimer : IDisposable {
        private readonly TimerCallback _realCallback;
        private readonly Timer _timer;
        private TimeSpan _period;
        private DateTime _next;

        public CustomTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period) {
            _timer = new Timer(Callback, state, dueTime, period);
            _realCallback = callback;
            _period = period;
            _next = DateTime.Now.Add(dueTime);
        }

        private void Callback(object state) {
            _next = DateTime.Now.Add(_period);
            _realCallback(state);
        }

        public TimeSpan Period => _period;
        public DateTime Next => _next;
        public TimeSpan DueTime => _next - DateTime.Now;
        public bool Change(TimeSpan dueTime, TimeSpan period) {
            _period = period;
            _next = DateTime.Now.Add(dueTime);
            return _timer.Change(dueTime, period);
        }

        public void Dispose() => _timer.Dispose();
    }

    class TimerState {
        public CustomTimer _timer;
        public Checkers.ZDEInstallerInfo zdeInstallerInfo;
    }
}
