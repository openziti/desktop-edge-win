var ZitiService = {
    data: [],
    sort: "Name",
    sortHow: "ASC",
    init: function() {
        this.data = [];
    },
    events: function() {
        $("#ServiceUrl").click(ZitiService.urlClicked);
    },
    setSort: function(sort) {
        ZitiService.sort = sort;
        $("#ServiceSort").html(sort);
        $("#IdServiceSort").html(sort); 
        this.refresh();
    },
    setHow: function(how) {
        ZitiService.sortHow = how;
        if (how=="ASC") $("#SortHow").html(locale.get("Ascending"));
        else $("#SortHow").html(locale.get("Descending"));
        this.refresh();
    },
    urlClicked: function(e) {
        var obj = $(e.currentTarget);
        var protocol = obj.data("protocol");
        var url = obj.data("url");
        var port = Number(obj.data("port"));
        if (port==80) app.openUrl("http://"+url);
        else if (port==443) app.openUrl("https://"+url);
    },
    GetFirstHostName: function(addresses) {
        var host = "";
        var foundHost = false;
        for (var i=0; i<addresses.length; i++) {
            if (addresses[i].HostName!=null) {
                host = addresses[i].HostName;
                foundHost = true;
                break;
            }
        }
        if (!foundHost && addresses.length>0) {
            host = addresses[0].IP;
        }
        return host;
    },
    add: function(id) {
        // Do nothing next event will add now
    },
    remove: function(id) {
        var list = [];
        for (var i=0; i<ZitiService.data.length; i++) {
            if (ZitiService.data[i].Id!=id) {
                list.push(ZitiService.data[i]);
            }
        }
        ZitiService.data = list;
    },
    set: function(id, services) {
        for (var i=0; i<services.length; i++) {
            if (!ZitiService.isDefined(id, services[i].Id)) {

                services[i].FingerPrint = id;
                services[i].TotalPostureChecks = ((services[i].PostureChecks)?services[i].PostureChecks.length:0);
                services[i].Launch = "";
                var ports = "";

                var address = "";
                if (services[i].Addresses.length>0) address = ZitiService.GetFirstHostName(services[i].Addresses);
                services[i].Address = address;
                
                if (services[i].Ports.length>0) {
                    for (var j=0; j<services[i].Ports.length; j++) {
                        var port = services[i].Ports[j];
                        ports += ((j>0)?", ":"");
                        if (port.High==port.Low) {
                            ports += port.High;
                            if (port.High==80) services[i].Launch = "URL|http://"+address;
                            if (port.High==443) services[i].Launch = "URL|https://"+address;
                            if (port.High==3389) services[i].Launch = "RDP|"+address;
                            if (port.High==445) services[i].Launch = "FILE|"+address;
                        } else ports += port.Low+"-"+port.High;
                    }
                }
                services[i].Port = ports;

                var protocol = "";
                if (services[i].Protocols.length>0) {
                    for (var j=0; j<services[i].Protocols.length; j++) {
                        protocol += ((j>0)?", ":"")+services[i].Protocols[j];
                    }
                }
                services[i].Protocol = protocol;
                ZitiService.data.push(services[i]);
            }
        }
    },
    search: function(filter) {
        var results = [];
        for (var i=0; i<ZitiService.data.length; i++) {
            if (ZitiService.isMatch(ZitiService.data[i], filter)) results.push(ZitiService.data[i]);
        }
        return results;
    },
    isMatch: function(item, search) {
        search = search.trim().toLowerCase();
        if (search.length==0) return true;
        var terms = search.split(' ');
        for (var i=0; i<terms.length; i++) {
            var term = terms[i];
            if (item.Name.toLowerCase().indexOf(term)>=0) return true;
            for (var j=0; j<item.Addresses.length; j++) {
                if (item.Addresses[0].HostName) {
                    if (item.Addresses[0].HostName.toLowerCase().indexOf(term)>=0) return true;
                }
            }
            for (var j=0; j<item.Protocols.length; j++) {
                if (item.Protocols[0].toLowerCase().indexOf(term)>=0) return true;
            }
            var termNum = Number(term);
            if (!isNaN(Number(termNum))) {
                for (var j=0; j<item.Ports.length; j++) {
                    if (item.Ports[0].High==termNum || item.Ports[0].Low==termNum) return true;
                }
            }
        }
        return false;
    },
    refresh: function() {
        $("#ServiceCount").html(ZitiService.data.length);
        $("#NavServiceCount").html(ZitiService.data.length);

        $("#ServiceList").html("");
        $("#FullServiceList").html("");

        if (!ZitiService.sort) ZitiService.sort = $("#IdServiceSort").html();
        if (!ZitiService.sort || ZitiService.sort=='') {
            ZitiService.sort = "Name";
            $("#IdServiceSort").html("Name");
        }

        if (ZitiService.sortHow=="ASC") {
            ZitiService.data = ZitiService.data.sort((a, b) => {
                var prop = ZitiService.sort.split(' ').join('');
                var propA = a[prop];
                var propB = b[prop];
                if (propA && propA!='' && isNaN(propA)) propA = propA.toLowerCase();
                if (propB && propB!='' && isNaN(propB)) propB = propB.toLowerCase();
                if (propA < propB) return -1;
                if (propA > propB) return 1;
                return 0;
            });
        } else {
            ZitiService.data = ZitiService.data.sort((a, b) => {
                var prop = ZitiService.sort.split(' ').join('');
                var propA = a[prop];
                var propB = b[prop];
                if (propA && propA!='' && isNaN(propA)) propA = propA.toLowerCase();
                if (propB && propB!='' && isNaN(propB)) propB = propB.toLowerCase();
                if (propA > propB) return -1;
                if (propA < propB) return 1;
                return 0;
            });
        }
        var mainlist = [];

        var opened = $(".identities.selected").data("id");
        for (var i=0; i<ZitiService.data.length; i++) {
            var item = ZitiService.data[i];

            var element = $("#ServiceItem").clone();
            element.removeClass("template");
            element.attr("id", "ServiceRow" + i);
            element.data("id", item.Id);
            var filter = $("#FilterId").val();
            var filterList = $("#FilterServices").val();

            if (item.FingerPrint==opened) {
                if (ZitiService.isMatch(item, filter)) element.addClass("open");
            }
            
            var fullElement = $("#FullServiceItem").clone();
            fullElement.removeClass("template");
            fullElement.attr("id", "FullServiceRow" + i);
            fullElement.attr("data-id", item.Id);

            if (ZitiService.isMatch(item, filterList)) {
                fullElement.addClass("open");
            }

            var postureStatus = "pass";
            var postureStyle = "";

            if (item.PostureChecks!=null && Array.isArray(item.PostureChecks) && item.PostureChecks.length>0) {
                postureStatus = "pass";
                postureStyle = "";
                for (var j=0; j<item.PostureChecks.length; j++) {
                    var check = item.PostureChecks[j];
                    if (!check.IsPassing) {
                        postureStatus = "fail";
                        postureStyle = "error";
                        break;
                    }
                }
            }

            if (item.Launch.length>0) {
                element.find(".icon").addClass("clickable");
                element.find(".icon").attr("data-launch", item.Launch);
            }
            element.html(element.html().split("{{address}}").join(item.Address));
            element.html(element.html().split("{{ports}}").join(item.Port));
            element.html(element.html().split("{{protocols}}").join(item.Protocol));
            element.html(element.html().split("{{postureStyle}}").join(postureStyle));

            fullElement.html(fullElement.html().split("{{address}}").join(item.Address));
            fullElement.html(fullElement.html().split("{{ports}}").join(item.Port));
            fullElement.html(fullElement.html().split("{{protocols}}").join(item.Protocol));
            fullElement.html(fullElement.html().split("{{postureStatus}}").join(postureStatus));
            fullElement.html(fullElement.html().split("{{postureStyle}}").join(postureStyle));
            if (i==0) fullElement.addClass("selected");

            for (var prop in item) {
                element.html(element.html().split("{{"+prop+"}}").join(ZitiService.getValue(item[prop])));
                fullElement.html(fullElement.html().split("{{"+prop+"}}").join(ZitiService.getValue(item[prop])));
            }
            $("#ServiceList").append(element);
            if (!mainlist.includes(item.Id)) {
                mainlist.push(item.Id);
                $("#FullServiceList").append(fullElement);
            }

            if ($("#ServiceList").find(".open").length==0) $("#IdServiceFilterArea").removeClass("open");
            else $("#IdServiceFilterArea").addClass("open");

        }
        $(".clickable").click(ZitiService.launch);
        ZitiService.showDetails();
        $(".fullservices").click((e) => {
            $(".fullservices").removeClass("selected");
            $(e.currentTarget).addClass("selected");
            ZitiService.showDetails();
        });
        $(".clicker").click((e) => {
            var id = $(e.currentTarget).data("id");
            app.showScreen("ServiceScreen");
            $(".fullservices").removeClass("selected");
            $(".fullservices[data-id='"+id+"']").addClass("selected");
            ZitiService.showDetails();
        });
        $(".services").click((e) => {
            $(".services").removeClass("selected");
            $(e.currentTarget).addClass("selected");
        });
        dragging.init();
    },
    launch: function(e) {
        var launcher = $(e.currentTarget).data("launch");
        var items = launcher.split('|');
        if (items.length==2) {
            if (items[0]=="URL") app.openUrl(items[1]);
            else if (items[0]=="FILE") {
                app.openPath("\\\\"+items[1]+"\\");
                ipcRenderer.invoke("window", "minimize");
            } else if (items[0]=="RDP") child.exec("mstsc /v:"+items[1]);
        }
    },
    showDetails: function() {
        if ($(".fullservices.selected").length>0) {
            var id = $(".fullservices.selected").data("id");
            var item = ZitiService.getById(id);
            var address = "";
            var firstProtocol = "";
            var firstHost = "";
            var firstPort = "";
            if (item.Protocols && item.Protocols.length>0) {
                var protocols = "";
                for (var j=0; j<item.Protocols.length; j++) {
                    if (firstProtocol=="") firstProtocol = item.Protocols[j];
                    address += ((j>0)?", ":"")+item.Protocols[j];
                    protocols += ((j>0)?", ":"")+item.Protocols[j];
                }
                address += "://";
                $("#ServiceProtocols").html(protocols);
            }
            if (item.Addresses && item.Addresses.length>0) {
                var addreses = "";
                for (var j=0; j<item.Addresses.length; j++) {
                    var val = "";
                    if (item.Addresses[j].HostName) {
                        if (firstHost=="") firstHost = item.Addresses[j].HostName;
                        val = item.Addresses[j].HostName;
                    } else if (item.Addresses[j].IP) val = item.Addresses[j].IP;

                    address += val;
                    if (item.Launch.length>0) {
                        addreses += ((j>0)?'<br/>':'')+'<span class="detailClick" data-launch="'+item.Launch+'">'+val+'</span>';
                    } else {
                        addreses += ((j>0)?'<br/>':'')+'<span>'+val+'</span>';
                    }
                }
                address += ":";
                $("#ServiceAddresses").html(addreses);
                if (firstHost=="") firstHost = item.Addresses[0].IP;
            }
            $(".detailClick").click(ZitiService.launch);
            if (item.Ports && item.Ports.length>0) {
                var ports = "";
                for (var j=0; j<item.Ports.length; j++) {
                    var port = item.Ports[j];
                    ports += ((j>0)?", ":"");
                    if (port.High==port.Low) ports += port.High;
                    else ports += port.Low+"-"+port.High;
                    if (firstPort=="") firstPort = port.Low;
                    address += ports;
                }
                $("#ServicePorts").html(ports);
            }
            var identity = ZitiIdentity.getById(item.FingerPrint);
            $("#ServiceIdentity").val(identity.Name);
            $("#PostureChecks").html("");
            if (item.PostureChecks!=null && Array.isArray(item.PostureChecks) && item.PostureChecks.length>0) {
                postureStatus = "pass";
                postureStyle = "green";
                $("#PassFail").removeClass("fail");
                $("#PassFail").html("pass");
                for (var j=0; j<item.PostureChecks.length; j++) {
                    var check = item.PostureChecks[j];
                    if (j>0)  $("#PostureChecks").append('<br/>');
                    if (!check.IsPassing) {
                        $("#PostureChecks").append('<span class="striken">'+check.QueryType+'</span>');
                        $("#PassFail").addClass("fail");
                        $("#PassFail").html("fail");
                        postureStatus = "fail";
                        postureStyle = "red";
                    } else {
                        $("#PostureChecks").append(check.QueryType);
                    }
                }
                $("#PassFail").show();
            } else {
                $("#PostureChecks").html("None");
                $("#PassFail").hide();
            }
    
            $("#ServiceName").val(item.Name);
            var url = "";
            url += firstProtocol+"://"+firstHost+":"+firstPort;

            $("#ServiceUrl").removeClass("openable");
            $("#ServiceUrl").val(url);
        }
    },
    getValue: function (item) {
        if (item!=null) return item;
        else return "";
    },
    getById: function(id) {
        for (var i=0; i<ZitiService.data.length; i++) {
            var item = ZitiService.data[i];
            if (item.Id==id) return item;
        }
        return null;
    },
    isDefined: function(fingerprint, id) {
        for (var i=0; i<this.data.length; i++) {
            if (this.data[i].FingerPrint==fingerprint && this.data[i].Id==id) return true;
        }
        return false;
    }
}