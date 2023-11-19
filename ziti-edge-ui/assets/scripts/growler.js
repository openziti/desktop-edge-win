var growler = {
  isDebugging: false,
  showId: -1,
  data: [],
  init: function() {
    $("body").append('<div id="Growler" class="growler"><div class="subtitle"></div><div class="icon"></div></div>');
    growler.events();
    if (!growler.data) growler.data = [];
  },
  events: function() {
    $("#AlertButton").click(growler.toggle);
    $("#NotificationMenuClose").click(growler.toggle);
  },
  toggle: function(e) {
    if ($("#NotificationsMenu").hasClass("open")) $("#NotificationsMenu").removeClass("open");
    else {
      growler.loadLogs();
      header.openIfClosed();
      $("#NotificationsMenu").addClass("open");
    }
  },
  clear: function() {
    context.remove("growlers");
    growler.data = [];
    $("#AlarmCount").hide();
    growler.loadLogs();
  },
  loadLogs: function() {
    $("#NotificationsList").html("");
    if (growler.data.length>0) {
      growler.data = growler.data.reverse();
      for (var i=0; i<growler.data.length; i++) {
          var element = $("#NotificationTemplate").clone();
          element.removeClass("template");
          element.attr("id","Row"+i);
          element.addClass(growler.data[i].type);
          element.html(element.html().split("{{type}}").join(growler.data[i].type));
          element.html(element.html().split("{{level}}").join(growler.data[i].title));
          element.html(element.html().split("{{subtitle}}").join(growler.data[i].subtitle));
          element.html(element.html().split("{{message}}").join(growler.data[i].message));
          element.html(element.html().split("{{time}}").join(moment(growler.data[i].time).fromNow()));
          $("#NotificationsList").append(element);
      }
			$("#AlarmCount").show();
      $("#ClearNotificationsButton").show();
    } else {
			$("#AlarmCount").hide();
      $("#ClearNotificationsButton").hide();
      $("#NotificationsList").html("<span class='nonotify'>No Notifications to Display</span>")
    }
  },
	show: function(type, title, subtitle, message) {
    if (growler.showId!=-1) clearTimeout(growler.showId);
    $("#Growler").removeClass("open");
    $("#Growler").removeClass("success");
    $("#Growler").removeClass("error");
    $("#Growler").removeClass("warning");
    $("#Growler").removeClass("info");
    $("#Growler").removeClass("bug");
    if (type!="debug"||growler.isDebugging) {
      $("#Growler").addClass(type);
      $("#Growler").find(".title").html(title);
      $("#Growler").find(".subtitle").html(subtitle);
      $("#Growler").find(".content").html(message);
      $("#Growler").addClass("open");
      growler.showId = setTimeout(function() {
        growler.showId = -1;
        $("#Growler").removeClass("open");
      }, 5000);
    }
	},
	error: function(subtitle, message) {
		growler.show("error",locale.get("ErrorTitle"), subtitle, message);
  },
  info: function(subtitle, message) {
		growler.show("info",locale.get("Information"), subtitle, message);
  },
  debug: function(subtitle, message) {
		growler.show("debug",locale.get("Debugger"), subtitle, message);
  },
  warning: function(subtitle, message) {
		growler.show("warning",locale.get("WarningMessage"), subtitle, message);
  },
  bug: function(subtitle, message) {
		growler.show("bug",locale.get("SystemBug"), subtitle, message);
  },
  success: function(subtitle, message) {
		growler.show("success",locale.get("Success"), subtitle, message);
  },
  form: function() {
    growler.error(locale.get("InvalidForm"), locale.get("CorrectFields"))
  }
}