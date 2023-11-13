
var mfa = {
    Data: {},
    MfaCodes: [],
    init: function() {
        mfa.events();
    },
    events: function() {
        $("#MfaLinkButton").click(mfa.link);
        $("#MfaSecretButton").click(mfa.secret);
        $("#MfaQrButton").click(mfa.hideSecret);
        $("#AuthenticateButton").click(mfa.verify);
        $("#SaveCodesButton").click(mfa.save);
        $("#MfaStatus").click(mfa.showAuthenticate);
        $("#MfaTimeout").click(mfa.showAuthenticate);
        $("#ReAuthenticateButton").click(mfa.authenticate);
        $("#RemoveMfaButton").click(mfa.remove);
        $("#RecoveryMfaButton").click(mfa.recoveryAuth);
    },
    toggle: function(e) {
        let identity = ZitiIdentity.selected();
        if (identity.MfaEnabled) {
            $("#RemoveCode").val("");
            modal.show("MfaRemoveModal");
        } else {
            var mfaData = {
                Command: "EnableMFA",
                Data: {
                    Identifier: identity.Identifier
                }
            };
            app.sendMessage(mfaData);
        }
    },
    remove: function() {
        let identity = ZitiIdentity.selected();
        var code = $("#RemoveCode").val().trim();
        if (identity) {
            if (code.length>=6) {
                var mfaData = {
                    Command: "RemoveMFA",
                    Data: {
                        Identifier: identity.Identifier,
                        Code: code
                    }
                };
                app.sendMessage(mfaData);
            } else growler.error(locale.get("InvalidMFACode"));
        }
    },
    link: function(e) {
        app.openUrl(mfa.Data.ProvisioningUrl);
    },
    save: function(e) {
        let identity = ZitiIdentity.selected();
        ipcRenderer.invoke("action-save", { id: identity.Name, codes: mfa.MfaCodes[identity.FingerPrint]});
    },
    hideSecret: function(e) {
        $("#MfaSecretButton").show();
        $("#MfaQrButton").hide();
        $("#MfaSecret").hide();
        $("#QrCode").show();
    },
    secret: function(e) {
        $("#MfaSecretButton").hide();
        $("#MfaQrButton").show();
        $("#MfaSecret").show();
        $("#QrCode").hide();
    },
    recoveryAuth: function() {
        let identity = ZitiIdentity.selected();
        var code = $("#RecoveryCode").val().trim();
        if (identity) {
            if (code.length>=6) {
                var command = {
                    Command: "GetMFACodes",
                    Data: {
                        Identifier: identity.Identifier,
                        Code: code
                    }
                }
                app.sendMessage(command);
                app.startAction("GetMFACodes");
                ui.showLoad();
            } else growler.error(app.keys.InvalidMFACode);
        }
    },
    recoveryCodes: function() {
        let identity = ZitiIdentity.selected();
        $("#RecoveryCodeList").html("");
        if (identity) {
            if (mfa.MfaCodes[identity.FingerPrint]!=null && mfa.MfaCodes[identity.FingerPrint].length>0) {
                var codes = mfa.MfaCodes[identity.FingerPrint];
                for (var i=0; i<codes.length; i++) {
                    $("#RecoveryCodeList").append("<div>"+codes[i]+"</div>");
                }
                modal.show("MfaRecoveryModal");
            } else {
                $("#RecoveryCode").val("");
                modal.show("MfaRecoveryAuthModal");
            }
        }
    },
    authorize: function() {
        let identity = ZitiIdentity.selected();
        var code = $("#AuthCode").val().trim();
        if (identity) {
            if (code.length>=6) {
                var command = {
                    Command: "SubmitMFA",
                    Identifier: identity.Indentifier,
                    Code: code
                }
                app.sendMessage(command);
            } else growler.error(app.keys.InvalidMFACode);
        }
    },
    verify: function() {
        let identity = ZitiIdentity.selected();
        var code = $("#MfaSetupCode").val().trim();
        if (identity) {
            if (code.length>=6) {
                var command = {
                    Command: "VerifyMFA", 
                    Data: {
                        Identifier: identity.Identifier,
                        Code: code
                    }
                };
                app.sendMessage(command);
            } else growler.error(app.keys.InvalidMFACode);
        }
    },
    showAuthenticate: function() {
        let identity = ZitiIdentity.selected();
        if (identity.MfaNeeded) {
            $("#AuthCode").val("");
            modal.show("MfaAuthModal");
        } else {
            mfa.recoveryCodes();
        }
    },
    authenticate: function() {
        let identity = ZitiIdentity.selected();
        var code = $("#AuthCode").val().trim();
        if (identity) {
            if (code.length>=6) {
                var command = {
                    Command: "SubmitMFA", 
                    Data: {
                        Identifier: identity.Identifier,
                        Code: code
                    }
                };
                app.sendMessage(command);
            } else growler.error(app.keys.InvalidMFACode);
        }
    },
    setup: function(message, identity) {
        mfa.Data = message; 
        mfa.MfaCodes[identity.FingerPrint] = mfa.Data.RecoveryCodes;
        mfa.hideSecret();
        $("#MfaSetupId").html(identity.Name);
        mfa.qr(message.ProvisioningUrl);
        var params = new URLSearchParams(message.ProvisioningUrl);
        $("#MfaSecret").html(params.get("secret"));
        modal.show("MfaSetupModal");
    },
    qr: function(data) {
        $("#QrCode").html("");
        var qrcode = new QRCode("QrCode", {
            text: data,
            width: 170,
            height: 170,
            colorDark : "#000000",
            colorLight : "#ffffff",
            correctLevel : QRCode.CorrectLevel.M
        });
    }
}