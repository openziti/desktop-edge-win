using System.Diagnostics;
using ZitiUpdateService;

namespace ZitiUpdateService.Tests;

[TestClass]
public class MiniDumpTests {

    private const uint StreamThreadInfoList = 17;
    private const uint StreamModuleList = 4;

    private string _dir = "";

    [TestInitialize]
    public void Setup() {
        _dir = Path.Combine(Path.GetTempPath(), "minidump-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>Returns the stream types present in a minidump.</summary>
    private static HashSet<uint> StreamTypes(string path) {
        byte[] b = File.ReadAllBytes(path);
        Assert.AreEqual("MDMP", System.Text.Encoding.ASCII.GetString(b, 0, 4), "not a minidump");

        uint count = BitConverter.ToUInt32(b, 8);
        uint dir = BitConverter.ToUInt32(b, 12);
        var types = new HashSet<uint>();
        for (int i = 0; i < count; i++) {
            types.Add(BitConverter.ToUInt32(b, (int)dir + (i * 12)));
        }
        return types;
    }

    [TestMethod]
    public void CreateMemoryDump_WritesAMinidumpWithThreadInfo() {
        string dump = Path.Combine(_dir, "self.stalled.dmp");

        bool ok = MiniDump.CreateMemoryDump(Process.GetCurrentProcess(), dump);

        Assert.IsTrue(ok, "CreateMemoryDump reported failure");
        Assert.IsTrue(File.Exists(dump), "no dump was written");
        Assert.IsTrue(new FileInfo(dump).Length > 0, "dump is empty");

        // The ThreadInfoList stream is only emitted when MiniDumpWithThreadInfo (0x1000) is passed.
        // The service shipped 0x10 for years, which is MiniDumpScanMemory - a different flag that
        // makes debuggers scan raw stack memory instead of unwinding it. Asserting on the stream
        // rather than on the constant means a wrong flag cannot satisfy this test.
        Assert.IsTrue(StreamTypes(dump).Contains(StreamThreadInfoList),
            "dump has no ThreadInfoList stream, so MiniDumpWithThreadInfo was not in effect");
    }

    [TestMethod]
    public void CreateMemoryDump_RecordsModules() {
        string dump = Path.Combine(_dir, "self.stalled.dmp");

        Assert.IsTrue(MiniDump.CreateMemoryDump(Process.GetCurrentProcess(), dump));
        Assert.IsTrue(StreamTypes(dump).Contains(StreamModuleList), "dump has no module list");
    }

    [TestMethod]
    public void CreateMemoryDump_LeavesNoTempFileBehind() {
        string dump = Path.Combine(_dir, "self.stalled.dmp");

        MiniDump.CreateMemoryDump(Process.GetCurrentProcess(), dump);

        Assert.IsFalse(File.Exists(dump + ".tmp"), "the intermediate .tmp file was not cleaned up");
    }

    [TestMethod]
    public void CreateMemoryDump_WhenItFails_KeepsThePreviousDump() {
        string dump = Path.Combine(_dir, "previous.stalled.dmp");
        byte[] previous = System.Text.Encoding.ASCII.GetBytes("AN EARLIER DUMP");
        File.WriteAllBytes(dump, previous);

        // A process that has already exited cannot be dumped, so this is a deterministic failure
        // that does not depend on the test running elevated.
        Process dead = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        dead.WaitForExit();

        bool ok = MiniDump.CreateMemoryDump(dead, dump);

        Assert.IsFalse(ok, "dumping an exited process should fail");
        // Writing straight to the destination truncates it first, so this used to leave the caller
        // with 0 bytes and the earlier dump destroyed - which is what four of the nine machines on
        // the ticket that prompted this actually shipped.
        CollectionAssert.AreEqual(previous, File.ReadAllBytes(dump), "the previous dump was destroyed");
        Assert.IsFalse(File.Exists(dump + ".tmp"), "a partial .tmp was left behind");
    }
}
