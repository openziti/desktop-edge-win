
var Log = {
    error: function(from, message) {
        Log.write("error", from, message);
    },
    warn: function(from, message) {
        Log.write("warn", from, message);
    },
    info: function(from, message) {
        Log.write("info", from, message);
    },
    debug: function(from, message) {
        Log.write("debug", from, message);
    },
    verbose: function(from, message) {
        Log.write("verbose", from, message);
    },
    trace: function(from, message) {
        Log.write("trace", from, message);
    },
    write: function(level, from, message) {
        var log = {
            level: level,
            from: from,
            message: message
        }
        ipcRenderer.invoke("log", log);
    }
}