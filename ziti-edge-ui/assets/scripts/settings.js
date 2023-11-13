var ZitiSettings = {
    Ip: "",
    Subnet: "",
    MTU: "",
    DNS: "",
    TunIpv4: "",
    TunIpv4Mask: "",
    AddDns: false,
    ApiPageSize: 25,
    init: function(obj) {
        this.AddDns = obj.AddDns;
        this.Subnet = obj.IpInfo.Subnet;
        this.MTU = obj.IpInfo.MTU;
        this.DNS = obj.IpInfo.DNS;
        this.Ip = obj.TunIpv4;
        this.TunIpv4Mask = obj.TunIpv4Mask;
        this.AddDns = obj.AddDns;
        this.ApiPageSize = obj.ApiPageSize;
        
        $("*[data-level='"+obj.LogLevel+"']").addClass("selected");

        $("#EditIP").val(ZitiSettings.Ip);
        $('#EditSubnet option:contains("'+ZitiSettings.Subnet+'")').attr('selected', true);
        $("#EditAPI").val(ZitiSettings.ApiPageSize);
        $("#EditDNS").removeClass("on");
        if (ZitiSettings.AddDns) $("#EditDNS").addClass("on");

        $("#SettingIp").html(ZitiSettings.Ip);
        $("#SettingSubnet").html(ZitiSettings.Subnet);
        $("#SettingMTU").html(ZitiSettings.MTU);
        $("#SettingDNS").html(ZitiSettings.AddDns?"enabled":"disabled");
        $("#SettingAPI").html(ZitiSettings.ApiPageSize);

        if (obj.Active) {
            ui.hideLoad();
        }
    }
}