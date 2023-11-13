var ui = {
    hello: null,
    timerInterval: null,
    seconds: 0,
    isOn: false,
    isFirst: true,
    outTimeout: null,
    init: function() {
        ui.events();
        ui.animate();
    },
    events: function() {
        $("#OnOffButton").click(ui.power);
        $(".releaseStream").click(ui.updateRelease);
        $("#UpdateAvailable").click(ui.forceUpdate);
        $(".sliderLeft").mouseover(ui.slideOpen);
        $(".area").mouseover(() => {
            ui.slideClosed();
        });
        $("nav").mouseover(ui.slideClosed);
        window.onblur = function(){
            ui.slideClosed();
        }
    },
    updates: function(data) {
        $(".releaseStream").removeClass("selected");
        if (data.ReleaseStream=="stable") $(".releaseStream[data-id='Normal']").addClass("selected");
        else $(".releaseStream[data-id='Beta']").addClass("selected");
        if (data.AutomaticUpgradeDisabled.toLowerCase()=='false') $("#UpdateOn").addClass("on");
        else $("#UpdateOn").removeClass("on");
    },
    slideOpen: function(e) {
        console.log("Over It");
        if (!$("body").hasClass("max")) {
            $("main").addClass("slid");
        }
    },
    slideClosed: function(e) {
        if (ui.outTimeout) {
            clearTimeout(ui.outTimeout);
            ui.outTimeout = null;
        }
        $("main").removeClass("slid");
    },
    state: function(data) {
        if (data.Active!=ui.isOn || ui.isFirst) {
            ui.isFirst = false;
            ui.isOn = data.Active;
            if (data.ServiceVersion&&data.ServiceVersion.Version) $("#ServiceVersion").html(data.ServiceVersion.Version);
            if (ui.isOn) {
                if (!$("#OnOffButton").hasClass("on")) {
                    if (ui.timerInterval) clearInterval(ui.timerInterval);
                    ui.seconds = Math.ceil(data.Duration/1000);
                    
                    ui.timerInterval = setInterval(ui.tick, 1000);
                    $("#OnOffButton").addClass("on");
                    $("#CircleArea").show();
                    $("#WelcomeBadge").hide();
                    $(".serviceon").show();
                    $(".serviceoff").hide();
                }
            } else {
                ui.seconds = 0;
                $("#UploadSpeed").html("0.0");
                $("#DownloadSpeed").html("0.0");
                $("#OnOffButton").removeClass("on");
                $("#CircleArea").hide();
                $("#WelcomeBadge").show();
                $(".serviceon").hide();
                $(".serviceoff").show();
                $("#IdentityScreenArea").addClass("forceHide");
                $("#ServiceScreenArea").addClass("forceHide");
                $("#NoDataIdentityScreen").removeClass("forceHide");
                $("#NoDataServiceScreen").removeClass("forceHide");
            }
        }
    },
    notification: function(data) {
        var now = moment();
        var installDate = moment(data.InstallTime);
        var message = "";
        var props = {
            version: data.ZDEVersion,
            date: installDate.format("MM/DD/YYYY hh:mm A")
        };
        if ($("#UpdateOn").hasClass("on")) {
            if (installDate.diff(now, 'seconds') < 60) {
                message = locale.get("UpdateAlert");
            } else {
                message = locale.getReplace("UpdateDate", props);
            }
        } else {
            message = locale.getReplace("UpdateVersion", props);
        }
        $("#UpdateAvailable").show();
        var notify = new Notification("Update", { appID: "Ziti Desktop Edge", body: message, tag: "update", icon: path.join(__dirname, '/assets/images/ziti-white.png') });
    },
    forceUpdate: function(e) {
        var command = {
            Op: "TriggerUpdate", 
            Action: ""
        };
        app.sendMonitorMessage(command);
    },
    updateRelease: function(e) {
        $(".releaseStream").removeClass("selected");
        $(e.currentTarget).addClass("selected");
    },
    showLoad: function() {
        $(".loader").show();
		$(".modal.background").addClass("open");
		$("body").addClass("hideScroll");
		$("main").addClass("disable");
		$("nav").addClass("disable");
    },
    hideLoad: function() {
        $(".loader").hide();
		$(".modal.background").removeClass("open");
		$("body").removeClass("hideScroll");
		$("main").removeClass("disable");
		$("nav").removeClass("disable");
    },
    updateConfig: function() {
        var isOn = $("#UpdateOn").hasClass("on");
        var command = {
            Op: "SetAutomaticUpgradeDisabled", 
            Action: !isOn
        };
        app.sendMonitorMessage(command);
    },
    power: function(e) {
        ui.showLoad();
        if ($("#OnOffButton").hasClass("on")) {
            if (ui.timerInterval) clearInterval(ui.timerInterval);
            ui.seconds = 0;
            ui.isOn = false;
            $("#UploadSpeed").html("0.0");
            $("#DownloadSpeed").html("0.0");
            // Show Loader Turn off Service
            app.sendMonitorMessage({ Op:"Stop", Action:"Normal" });
            $("#OnOffButton").removeClass("on");
            ui.state({Active: false});
            ZitiIdentity.data = [];
            ZitiService.data = [];
            $("#NavServiceCount").html("0");
            $("#NavIdentityCount").html("0");
            $("#CircleArea").hide();
            $("#WelcomeBadge").show();
            if (app.os=="linux") {
                // Do this here because there is no state coming once off on Linux
                $(".missions").hide();
                ui.hideLoad();
                ui.seconds = 0;
                $("#UploadSpeed").html("0.0");
                $("#DownloadSpeed").html("0.0");
                $("#OnOffButton").removeClass("on");
                $("#CircleArea").hide();
                $("#WelcomeBadge").show();
                $(".serviceon").hide();
                $(".serviceoff").show();
                $("#IdentityScreenArea").addClass("forceHide");
                $("#ServiceScreenArea").addClass("forceHide");
                $("#NoDataIdentityScreen").removeClass("forceHide");
                $("#NoDataServiceScreen").removeClass("forceHide");
                $("#DisconnectedState").show();
            }
        } else {
            // Show Loader Turn On Service
            app.sendMonitorMessage({ Op:"Start", Action:"Normal" });
            ui.timerInterval = setInterval(ui.tick, 1000);
            ui.isOn = true;
            $("#OnOffButton").addClass("on");
            $("#CircleArea").show();
            $("#WelcomeBadge").hide();
        }
    },
    tick: function() {
        ui.seconds++;
            
        var day = 86400;
        var hour = 3600;
        var minute = 60;
            
        var totalseconds = ui.seconds;

        var daysout = Math.floor(totalseconds / day);
        var hoursout = Math.floor((totalseconds - daysout * day)/hour);
        var minutesout = Math.floor((totalseconds - daysout * day - hoursout * hour)/minute);
        var secondsout = totalseconds - daysout * day - hoursout * hour - minutesout * minute;

        var label = "";
        if (daysout>0) label += daysout+":";
        if (hoursout<10) label += "0";
        label += hoursout+":";
        if (minutesout<10) label += "0";
        label += minutesout+":";
        if (secondsout<10) label += "0";
        label += secondsout;

        $("#Timer").html(label);
    },
    animate: function() {
        ui.hello = bodymovin.loadAnimation({
            container: document.getElementById('HelloZiggy'),
            renderer: 'svg',
            loop: true,
            autoplay: true,
            path: './assets/animations/helloziggy.json'
        });
    }
}