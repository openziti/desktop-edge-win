const remote = require('electron').remote;
const shell = require('electron').shell;
const screen = require('electron').screen;
const child = require("child_process");
const fs = require('fs');
const ipcRenderer = require('electron').ipcRenderer;
const path = require("path");
const { threadId } = require('worker_threads');
const rootPath = require('electron-root-path').rootPath;
window.$ = window.jQuery = require("./assets/scripts/jquery.js"); 
var Highcharts = require('highcharts');   
require('highcharts/modules/exporting')(Highcharts);  
const githubUrl = "https://get.openziti.io/zdew/latest.json";

var app = {
    settings: {},
    screenId: "MissionControl",
    filterId: null,
    actionId: null,
    keys: null,
    totalTransfer: 0,
    maxTransfer: 10000,
    upMetricsArray: [],
    downMetricsArray: [],
    downChart: null,
    upChart: null,
    os: '',
    init: function() {

        app.events();
        modal.init();
        menu.init();
        growler.init();
        ui.init();
        mfa.init();
        dragging.init();
        ZitiIdentity.init();
        ZitiService.init();

        $(".loader").hide();
    },
    events: function() {
        ipcRenderer.on('service-logs', app.onServiceLogs);
        ipcRenderer.on('message-to-ui', app.onData);
        ipcRenderer.on('os', app.setOS);
        ipcRenderer.on('locale', app.setLocale);
        ipcRenderer.on('version', app.setVersion);
        ipcRenderer.on('app-status', app.onStatus);
        ipcRenderer.on('service-down', app.down);
        ipcRenderer.on('goto', app.goto);
        ipcRenderer.on('loader', app.loader);
        ipcRenderer.on('growl', app.growl);
        $("[data-screen]").click(app.screen);
        $("[data-action]").click(app.action);
        $(".fullNav").click(app.sub);
        $(".supportNav").click(app.support);
        $("#EditButton").click(app.showForm);
        $("#CloseForm").click(app.hideForm);
        $(".levelSelect").click(app.levelSelect);
        $(".releaseStream").click(app.releaseStream);
        $(".toggle").click(app.toggle);
        $('[data-url]').click(app.open);
        $(".search").keyup(app.search);
        $("#FilterId").keyup(app.filterIdServices);
        $("#FilterServices").keyup(app.filterServices);
		$("input").on("keyup", app.enter);
		$("select").on("keyup", app.enter);
        $("#CheckUpdates").click(app.checkUpdate);
        $("#SaveConfigButton").click(app.save);
        $("#SaveUrlButton").click(app.setUpdateUrl);
        $("#CloseUrlForm").click(app.hideUrlForm);
        $("#EditUrlButton").click(app.showUrlForm);
        $("#ResetButton").click(app.resetUrl);
        $(".sort").click((e) => {
            var options = $(e.currentTarget).find(".options");
            if (options) {
                if (options.hasClass("open")) options.removeClass("open");
                else options.addClass("open");
            }
        });
        $(".option").click((e) => {
            var sortWhat = $(e.currentTarget).data("what");
            var sort = $(e.currentTarget).data("sort");
            var how = $(e.currentTarget).data("how");
            if (sort) {
                if (sortWhat=="identity") {
                    ZitiIdentity.setSort(sort);
                } else if (sortWhat=="service") {
                    ZitiService.setSort(sort);
                }
            }
            if (how) { 
                if (sortWhat=="identity") {
                    ZitiIdentity.setHow(how);
                } else if (sortWhat=="service") {
                    ZitiService.setHow(how);
                }
            }
        });
        $("#ClearSearch").click((e) => {
            $(".search").val("");
            $("#ClearSearch").removeClass("active");
            $("#GlobalResults").html("");
            $("#GlobalResults").removeClass("open");
        });
        $("main").click((e) => {
            if ($("#GlobalResults").hasClass("open")) $("#GlobalResults").removeClass("open");
        });
        $(".closeApp").click(app.close);
        $(".maximize").click((e) => {
            if ($("body").hasClass("max")) {
                $("body").removeClass("max");
                ipcRenderer.invoke("window", "unmaximize");
            } else {
                $("body").addClass("max");
                ipcRenderer.invoke("window", "maximize");
            }
        });
        $(".minimize").click((e) => {
            ipcRenderer.invoke("window", "minimize");
        });
        $("#HeaderArea").dblclick(app.toggleScreen);
        document.addEventListener ("keydown", function (zEvent) {
            if (zEvent.ctrlKey  &&  zEvent.altKey  &&  zEvent.key === "d") {  // case sensitive
                $("#ReleaseStream").show();
            }
        } );
    },
    checkUpdate: function(e) {
        app.sendMonitorMessage({
            Op: "DoUpdateCheck",
            Action: ""
        });
        growler.info(locale.get("Checking"));
        $("#CheckUpdates").addClass("disabled");
    },
    resetUrl: (e) => {
        $("#EditReleaseUrl").val(githubUrl);
    },
    releaseStream: (e) => {
        app.sendMonitorMessage({
            Op: "SetReleaseStream",
            Action: $(e.currentTarget).data("id")
        });
        growler.success("Release Stream Set");
    },
    setUpdateUrl: (e) => {
        app.sendMonitorMessage({
            Op: "SetAutomaticUpgradeURL",
            Action: $("#EditReleaseUrl").val()
        });
        growler.success("Url Set to "+$("#EditReleaseUrl").val());
        $("#EditReleaseUrl").val("");
        app.hideUrlForm();
    },    
    showUrlForm: function(e) {
        $("#EditUrlForm").addClass("open");
    },
    hideUrlForm: function(e) {
        $("#EditUrlForm").removeClass("open");
    },
    growl: function(e, data) {
        growler.error(data);
    },
    loader: function(e, data) {
        if (data) ui.showLoad();
        else ui.hideLoad();
    },
    goto: function(e, data) {
        console.log(e, data);
        if (data.to=="Identity") {
            ZitiIdentity.select(data.id);
            app.showScreen("IdentityScreen");
            if (data.mfa) mfa.showAuthenticate();
        }
    },
    toggleScreen: function() {
        if ($("body").hasClass("max")) {
            $("body").removeClass("max");
            ipcRenderer.invoke("window", "unmaximize");
        } else {
            $("body").addClass("max");
            ipcRenderer.invoke("window", "maximize");
        }
    },
    copy: function(e) {
        navigator.clipboard.writeText($(e.currentTarget).html());
        growler.success($(e.currentTarget).html()+" copied");
    },
    down: function(e, data) {
        ui.state({Active: false, from: "down"});
    },
    onServiceLogs: function(e) {
        $("#ServiceLogs").html(e);
    },
    setOS: function(e, data) {
        app.os = data;
        if (app.os=="win32") $(".windows").show();
        else if (app.os=="linux") $(".linux").show();
        else if (app.os=="darwin") $(".mac").show();
    },
    setLocale: function(e, data) {
        locale.init(data.toLowerCase());
    },
    setVersion: function(e, data) {
        $("#AppVersion").html(data);
    },
    enter: function(e) {
		if (e.keyCode == 13) {
			var id = $(e.currentTarget).data("enter");
			if ($("#"+id).length>0) $("#"+id).click();
		}
    },
    onStatus: function(e, data) {
        if (data.error) growler.error(data.error);
        else growler.success(data.status);
    },
    search: function(e) {
        var filter = $(e.currentTarget).val().trim();
        if (filter.length>0) {
            var identities = ZitiIdentity.search(filter);
            var services = ZitiService.search(filter);
            var html = "";
            if (identities.length>0) {
                html += '<div class="title">'+identities.length+' '+((identities.length>1)?locale.getLower("Identities"):locale.getLower("Identity"))+'</div>';
                for (var i=0; i<identities.length; i++) {
                    html += '<div class="result" data-type="identity" data-id="'+identities[i].FingerPrint+'">'+identities[i].Name+'</div>';
                }
            }
            if (services.length>0) {
                html += '<div class="title">'+services.length+' '+((services.length>1)?locale.getLower("Services"):locale.getLower("Service"))+'</div>';
                for (var i=0; i<services.length; i++) {
                    html += '<div class="result" data-service="identity" data-id="'+services[i].Id+'">'+services[i].Address+'</div>';
                }
            }
            $("#GlobalResults").html(html);
            $("#GlobalResults").addClass("open");
            $(".result").click((e) => {
                var id = $(e.currentTarget).data("id");
                var type = $(e.currentTarget).data("type");
                if (type=="identity") {
                    app.showScreen("IdentityScreen");
                    ZitiIdentity.select(id);
                } else {
                    app.showScreen("ServiceScreen");
                    $(".fullservices").removeClass("selected");
                    $(".fullservices[data-id='"+id+"']").addClass("selected");
                    ZitiService.showDetails();
                }
                $("#GlobalResults").html("");
                $("#GlobalResults").removeClass("open");
            });
            $("#ClearSearch").addClass("active");
        } else {
            $("#ClearSearch").removeClass("active");
            $("#GlobalResults").html("");
            $("#GlobalResults").removeClass("open");
        }
    },
    filterIdServices: function (e) {
        ZitiService.refresh();
    },
    filterServices: function (e) {
        ZitiService.refresh();
    },
    open: function(e) {
        var url = $(e.currentTarget).data("url");
        app.openUrl(url);
    },
    openUrl: function(url) {
        shell.openExternal(url);
    },
    openPath: function(path) {
        shell.openPath(path);
    },
    toggle: function(e) {
        if ($(e.currentTarget).hasClass("on")) $(e.currentTarget).removeClass("on");
        else $(e.currentTarget).addClass("on");
        var callAfter = $(e.currentTarget).data("call");
        if (callAfter=="mfa") mfa.toggle(); 
        else if (callAfter=="updates") ui.updateConfig(); 
    },
    releaseSelect: function(e) {
        $(".releaseStream.selected").removeClass("selected");
        $(e.currentTarget).addClass("selected");
    },
    levelSelect: function(e) {
        $(".levelSelect.selected").removeClass("selected");
        $(e.currentTarget).addClass("selected");
        var level = $(e.currentTarget).data("level");
        ipcRenderer.invoke("level", level);
    },
    close: function(e) {
        ipcRenderer.send('close');
    },
    showForm: function(e) {
        $("#EditForm").addClass("open");
    },
    hideForm: function(e) {
        $("#EditForm").removeClass("open");
    },
    screen: function(e) {
        var screen = $(e.currentTarget).data("screen");
        app.showScreen(screen);
    },
    showScreen: function(screen) {
        $("#FilterServices").val("");
        $("#FilterId").val("");
        modal.close();
        $("#"+app.screenId).removeClass("open");
        $("#"+screen).addClass("open");
        app.screenId = screen;
        $(".navItem").removeClass("selected");
        $(".missionBg").attr('class','missionBg');
        $(".missionBg").addClass(app.screenId);
        $("#OnOffButton").attr('class', '');
        if (ui.isOn) $("#OnOffButton").addClass("on");
        $("#OnOffButton").addClass(app.screenId);
        $("div.navItem[data-screen='"+screen+"']").addClass("selected");
        if (screen=="SupportScreen") $(".version").show();
        else $(".version").hide();
        if (screen=="IdentityScreen"||screen=="MissionControl") $("#AddButton").show();
        else $("#AddButton").hide();
        ZitiService.refresh();
    },
    sub: function(e) {
        var sub = $(e.currentTarget).data("sub");
        $("#AdvancedScreen").find(".sub").removeClass("open");
        $("#"+sub).addClass("open");
        $(".fullNav").removeClass("selected");
        $(e.currentTarget).addClass("selected");
    },
    support: function(e) {
        var sub = $(e.currentTarget).data("sub");
        $("#SupportScreen").find(".sub").removeClass("open");
        $("#"+sub).addClass("open");
        $(".supportNav").removeClass("selected");
        $(e.currentTarget).addClass("selected");
    },
    onData: function(event, data) {
        try {
            var message = JSON.parse(data);
            console.log(message);

            
            if (message.Op) {
                if (message.Op=="status") {
                    Log.debug("onData", "IPC In: "+message.Op);
                    Log.debug("onData", JSON.stringify(message));
                    for (var i=0; i<message.Status.Identities.length; i++) {
                        message.Status.Identities[i].Status = ((message.Status.Identities[i].Active)?locale.get("Active"):locale.get("Inactive"));
                        if (!message.Status.Identities[i].Services) message.Status.Identities[i].Services = [];
    
                        message.Status.Identities[i].TotalServices = message.Status.Identities[i].Services.length;
                    }
                    ZitiIdentity.set(message.Status.Identities);
                    for (var i=0; i<message.Status.Identities.length; i++) {
                        if (message.Status.Identities[i].Services) {
                            ZitiService.set(message.Status.Identities[i].FingerPrint, message.Status.Identities[i].Services);
                        }
                    }
                    ZitiSettings.init(message.Status);
                    console.log(ZitiIdentity.data);
                    ZitiService.refresh();
                    message.Status.from = "Status";
                    ui.state(message.Status);
                } else if (message.Op=="bulkservice") {
                    console.log("Here", message.RemovedServices, message.AddedServices);
                    if (message.RemovedServices) {
                        for (var i=0; i<message.RemovedServices.length; i++) {
                            var removed = message.RemovedServices[i];
                            console.log("Removing "+removed.Id);
                            ZitiService.remove(removed.Id);
                        }
                        ZitiService.refresh();
                    }
                    if (message.AddedServices) {
                        for (var i=0; i<message.AddedServices.length; i++) {
                            var added = message.AddedServices[i];
                            console.log("Adding "+added.Id);
                            ZitiService.add(added.Id);
                        }
                        ZitiService.refresh();
                    }
                } else if (message.Op=="metrics") {
                    app.metrics(message.Identities);
                } else if (message.Op=="identity") {
                    var id = message.Id;

                    var idSelected = ZitiIdentity.selected();
                    if (idSelected && id.FingerPrint==idSelected.FingerPrint) modal.close();

                    if (!id.Services) id.Services = [];
                    id.TotalServices = id.Services.length;
                    id.Status = ((id.Active)?"Active":"Inactive");

                    if (message.Action=="added") {
                        ZitiIdentity.add(id);
                    } else if (message.Action=="updated") {
                        ZitiIdentity.update(id);
                    }
                } else if (message.Op=="mfa") {
                    if (message.Action=="enrollment_challenge") {

                        // An enrollment request was made to the controller
                        var identity = ZitiIdentity.getByIdentifier(message.Identifier);
                        mfa.setup(message, identity);
                    } else if (message.Action=="enrollment_verification") {

                        // The Enrollment verification event
                        if (message.Successful) {
                            modal.hide();
                            mfa.recoveryCodes();
                        } else {
                            growler.error(locale.get("InvalidMFACode"))
                        }
                    } else if (message.Action=="enrollment_remove") {

                        // The Enrollment verification event
                        if (message.Successful) {
                            growler.success(locale.get("MFARemoved"));
                            ZitiIdentity.mfaRemoved();
                            $("#MfaStatus").removeClass("open");
                            modal.hide();
                            ui.hideLoad();
                        } else {
                            growler.error(locale.get("InvalidMFACode"))
                        }
                    } else if (message.Action=="auth_challenge") {
                        ZitiIdentity.SetMfaState(message.Fingerprint, message.Successful);
                        ZitiIdentity.refresh();
                        ZitiService.refresh();
                    } else if (message.Action=="mfa_auth_status") {
                        ZitiIdentity.SetMfaState(message.Fingerprint, message.Successful);
                        ZitiIdentity.refresh();
                        ZitiService.refresh();
                    }
                } else if (message.Op=="controller") {
                    if (message.Action=="disconnected") {
                        ZitiIdentity.SetControllerState(message.Fingerprint, false);
                    } else if (message.Action=="connected") {
                        ZitiIdentity.SetControllerState(message.Fingerprint, true);
                    }
                }
            } else {
                if (message.UpdateAvailable!=null) {
                    if (message.UpdateAvailable) {
                        growler.success(message.Message);
                    } else {
                        growler.info(message.Message);
                    }
                    $("#CheckUpdates").removeClass("disabled");
                } else {
                    if ((message.Code==2 || message.Code==-2) && message.Error!=null) {
                        growler.error(message.Error);
                    } else {
                        if (message.Message&&message.Message=="Stopped") {
                            ui.hideLoad();
                            ui.state({Active: false, from: "Message"});
                            ZitiIdentity.data = [];
                            ZitiService.data = [];
                            $("#NavServiceCount").html("0");
                            $("#NavIdentityCount").html("0");
                            ZitiIdentity.refresh();
                        } else {
                            if (message.Type=="Status") {
                                ui.updates(message);
                                if (message.Status&&message.Status=="Stopped") {
                                    ui.hideLoad();
                                    ui.state({Active: false, from: "Message"});
                                    ZitiIdentity.data = [];
                                    ZitiService.data = [];
                                    $("#NavServiceCount").html("0");
                                    $("#NavIdentityCount").html("0");
                                    ZitiIdentity.refresh();
                                } else {
                                    ui.hideLoad();
                                    if (message.Operation=="OnOff") {
                                        ui.state(message);
                                    }
                                }
                                if (message.Message&&message.Message=="Running") {
                                    ui.hideLoad();
                                    ui.state({Active: true, from: "Message"});
                                    ZitiIdentity.data = [];
                                    ZitiService.data = [];
                                    $("#NavServiceCount").html("0");
                                    $("#NavIdentityCount").html("0");
                                    ZitiIdentity.refresh();
                                }
                            } else if (message.Type=="Notification") {
                                ui.notification(message);
                             } else {
                                if (message.Success != null) {
                                    if (message.Error) growler.error(message.Error); 
            
                                    if (message.Data != null) {
                                        if (message.Data.Command !=null) {
                                            if (message.Data.Command=="RemoveIdentity") {
                                                $(".loader").hide();
                                                ZitiIdentity.forgotten(message.Data.Data.Identifier);
                                                growler.success(locale.get("IdentityForgotten"));
                                            } 
                                        } else {
                                            if (app.actionId=="GetMFACodes") {
                                                app.actionId = null;
                                                if (message.Data.RecoveryCodes!=null && message.Data.RecoveryCodes.length>0) {
                                                    ui.hideLoad();
                                                    let identity = ZitiIdentity.selected();
                                                    mfa.MfaCodes[identity.FingerPrint] = message.Data.RecoveryCodes;
                                                    modal.hide();
                                                    setTimeout(() => {
                                                        mfa.recoveryCodes();
                                                    }, 1000);
                                                }
                                            }
                                        }
                                    } else {
                                        if (app.actionId=="SaveConfig") {
                                            growler.warning(locale.get("ConfigSaved"));
                                            $("#EditForm").removeClass("open");
                                        }
                                    }
                                } else {
                                    if (app.actionId!=null) {
                                        if (message.Error!=null && message.Error.trim().length>0) {
                                            growler.error(message.Error);
                                            $(".loader").hide();
                                            $(".actionPending").removeClass("disabled");
                                        } else if (message.Message!=null && message.Message.trim().length>0) {
                                            $(".loader").hide();
                                            $(".actionPending").removeClass("disabled");
                                            if (app.actionId=="CaptureLogs") {
                                                shell.showItemInFolder(message.Message);
                                                growler.success("Package Generated");
                                            }
                                            app.actionId = null;
                                        }
                                    } else {
                                        ui.hideLoad();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        } catch (e) {
            Log.error("app.onData", e);
        }
    },
    metrics: function(identities) {
        var totalUp = 0;
        var totalDown = 0;
        var upscale = locale.get("kbps");
        var downscale = locale.get("kbps");
        for (var i=0; i<identities.length; i++) {
            totalUp += identities[i].Metrics.Up;
            totalDown += identities[i].Metrics.Down;
            ZitiIdentity.metrics(identities[i].FingerPrint, identities[i].Metrics.Up, identities[i].Metrics.Down);
        }
        app.totalTransfer = totalDown+totalDown;
        if (app.maxTransfer<app.totalTransfer) app.maxTransfer = app.totalTransfer;

        if (app.upMetricsArray.length>20) app.upMetricsArray.shift();
        if (app.downMetricsArray.length>20) app.downMetricsArray.shift();
        app.upMetricsArray.push(totalUp);
        app.downMetricsArray.push(totalDown);

        if (totalUp>1024) {
            totalUp = totalUp/1024;
            upscale = locale.get("mbps");
        }
        if (totalUp>1024) {
            totalUp = totalUp/1024;
            upscale = locale.get("gbps");
        }
        if (totalUp>1024) {
            totalUp = totalUp/1024;
            upscale = locale.get("tbps");
        }
        $("#UploadSpeed").html(totalUp.toFixed(1));
        $("#UploadMeasure").html(upscale);
        
        if (totalDown>1024) {
            totalDown = totalDown/1024;
            downscale = locale.get("mbps");
        }
        if (totalDown>1024) {
            totalDown = totalDown/1024;
            downscale = locale.get("gbps");
        }
        if (totalDown>1024) {
            totalDown = totalDown/1024;
            downscale = locale.get("tbps");
        }
        $("#DownloadSpeed").html(totalDown.toFixed(1));
        $("#DownloadMeasure").html(downscale);

        if (!app.downChart) {
            app.downChart = Highcharts.chart('DownloadGraph', {
                chart: { 
                    type: 'spline', 
                    backgroundColor: 'rgba(0,0,0,0)',
                    borderWidth: 0,
                    plotBackgroundColor: 'transparent',
                    plotShadow: false,
                    plotBorderWidth: 0,
                    margin: 0,
                    padding: 0,
                    spacing: [0, 0, 0, 0] 
                },
                exporting: { enabled: false },
                credits: { enabled: false },
                title: { text: ' '},
                subtitle: { text: ' ' },
                legend:{ enabled:false },
                yAxis: {
                    labels: { enabled: false },
                    title: { text: ' ' },
                    lineWidth: 0,
                    min: 0,
                    tickInterval: 100,
                    gridLineWidth: 0,
                    visible: false
                },
                xAxis: {
                    labels: { enabled: false },
                    title: { text: ' ' },
                    lineWidth: 0,
                    min: 0,
                    tickInterval: 100,
                    gridLineWidth: 0,
                    visible: false
                },
                tooltip: { enabled: false },
                plotOptions: {
                    series: {
                          lineColor: '#00DC5A',
                        states: {
                            hover: {
                                enabled: false
                            }
                        }
                    }
                },
                series: [ {
                    marker: {
                      enabled: false
                  },
                    name: '',
                  data: app.downMetricsArray
                }]
            });
        } else {
            app.downChart.series[0].update({
                data: app.downMetricsArray
            }, true);
        }
        if (!app.upChart) {
            app.upChart = Highcharts.chart('UploadGraph', {
                chart: { 
                    type: 'spline', 
                    backgroundColor: 'rgba(0,0,0,0)',
                    borderWidth: 0,
                    plotBackgroundColor: 'transparent',
                    plotShadow: false,
                    plotBorderWidth: 0,
                    margin: 0,
                    padding: 0,
                    spacing: [0, 0, 0, 0] 
                },
                exporting: { enabled: false },
                credits: { enabled: false },
                title: { text: ' '},
                subtitle: { text: ' ' },
                legend:{ enabled:false },
                yAxis: {
                    labels: { enabled: false },
                    title: { text: ' ' },
                    lineWidth: 0,
                    min: 0,
                    tickInterval: 100,
                    gridLineWidth: 0,
                    visible: false
                },
                xAxis: {
                    labels: { enabled: false },
                    title: { text: ' ' },
                    lineWidth: 0,
                    min: 0,
                    tickInterval: 100,
                    gridLineWidth: 0,
                    visible: false
                },
                tooltip: { enabled: false },
                plotOptions: {
                    series: {
                        lineColor: '#FFC400',
                        states: {
                            hover: {
                                enabled: false
                            }
                        }
                    }
                },
                series: [ {
                    marker: {
                    enabled: false
                },
                    name: '',
                data: app.downMetricsArray
                }]
            });
        } else {
            app.upChart.series[0].update({
                data: app.upMetricsArray
            }, true);
        }


    },
    action: function(e) {
        var id = $(e.currentTarget).data("action");
        Log.debug("app.action", id);
        ipcRenderer.invoke("action-"+id, "");
    },
    sendMessage: function(e) {
        Log.debug("app.sendMessage", e);
        console.log("Sending", e);
        ipcRenderer.invoke("message", e);
    },
    sendMonitorMessage: function(e) {
        Log.debug("app.sendMonitorMessage", e);
        console.log("Sending", e);
        ipcRenderer.invoke("monitor-message", e);
    },
    startAction: function(name) {
        app.actionId = name;
        $(".actionPending").addClass("disabled");
        $(".loader").show();
    },
    save: function(e) {
        app.actionId = "SaveConfig";
        var command = {
            Command: "UpdateTunIpv4", 
            Data: {
                TunIPv4: $("#EditIP").val(),
                TunPrefixLength: Number($("#EditSubnet").val()),
                AddDns: $("#EditDNS").hasClass("on"),
                ApiPageSize: Number($("#EditAPI").val())
            }
        };
        app.sendMessage(command);
    }
}

window.onerror = function(error, url, line) {
    console.log(error, url, line);
    ipcRenderer.send('errorInWindow', error);
}

$(document).ready(app.init);