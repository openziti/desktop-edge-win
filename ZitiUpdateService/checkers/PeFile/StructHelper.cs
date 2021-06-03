using System.Runtime.InteropServices;

namespace ZitiUpdateService.Checkers.PeFile {
    public static class StructHelper {

        public static T FromBytes<T>(byte[] bytes) {
            // Pin the managed memory while, copy it out the data, then unpin it
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T theStructure = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();

            return theStructure;
        }
    }
}
