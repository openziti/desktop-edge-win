
var menu = {
    init: function() {
        menu.events();
    },
    events: function() {
        $("#FeedbackButton").click(menu.Feedback);
        $("#GenerateButton").click(menu.GetLogPackage);
        $("#AppLogButton").click(menu.OpenAppLogs);
        $("#ServiceLogButton").click(menu.OpenServiceLogs);
    },
    Feedback: function() {
        shell.openExternal("mailto:support@openziti.org?subject=Desktop%20Edge%20Feedback");
    },
    GetLogPackage: function() {
        app.action = "CaptureLogs";
        var action = {
            Op: app.action,
            Action: "Normal"
        };
        app.startAction(app.action);
        app.sendMonitorMessage(action);
    },
    OpenServiceLogs: function(e) {
        if (app.os=="win32") ipcRenderer.invoke("open-service-logs");
        else ipcRenderer.invoke("logger-message", {});
    },
    OpenAppLogs: function(e) {
        ipcRenderer.invoke("open-logs");
    }
}