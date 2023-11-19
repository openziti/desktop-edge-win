var locale = {
    key: 'en-us',
    keys: [],
    init: function(language) {
        locale.key = language;
        var filePath = 'assets/languages/'+locale.key+'.json';
        var languageFile = path.join(__dirname, filePath);
        let obj = JSON.parse(fs.readFileSync(languageFile));

		for (var item in obj) {
			locale.keys[item] = obj[item];
		}
        
        if (fs.existsSync(path.join(__dirname, 'assets/languages/'+language+'.json'))) {
            filePath = 'assets/languages/'+language+'.json';
            obj = JSON.parse(fs.readFileSync(languageFile));
    
            for (var item in obj) {
                locale.keys[item] = obj[item];
            }
        }
        locale.loaded();
    },
    get: function(key) {
        if (!locale.keys[key]) return "";
        else return locale.keys[key];
    },
    getLower: function(key) {
        if (!locale.keys[key]) return "";
        else return locale.keys[key].toLowerCase();
    },
	loaded: function() {
		$("[data-i18n]").each((i, e) => {
			var key = $(e).data("i18n");
            var tag = $(e).prop("tagName").toLowerCase();
			if (key && key.trim().length>0) {
                if (tag=="input"||tag=="textarea") $(e).prop("placeholder", locale.keys[key]);
                else $(e).html(locale.keys[key]);
			} else {
				var id = $(e).attr("id");
                if (tag=="input"||tag=="textarea") $("#"+id).prop("placeholder", locale.keys[id]);
                else $("#"+id).html(locale.keys[id]);
			}
		});
	},
    getReplace(key, props) {
        let value = "";
        if (locale.keys.length==0) {
            locale.init(locale.key);
        }
        if (locale.keys[key]) {
            value = locale.keys[key];
            for (let prop in props) {
                if (!props[prop]) props[prop] = "";
                value = value.split("{{"+prop+"}}").join(props[prop]);
            }
        }
        return value;
    }
}