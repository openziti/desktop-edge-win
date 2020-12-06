using Newtonsoft.Json;
using System;
using System.Threading;

namespace ZitiDesktopEdge.Server {
    public class EventRegistry {
        protected static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        public static event EventHandler MyEvent;

        public static void SendEventToConsumers(object objToSend) {
            MyEvent?.Invoke(JsonConvert.SerializeObject(objToSend), null);
        }
    }
}
