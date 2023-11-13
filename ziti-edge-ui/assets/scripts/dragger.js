
var dragging = {
	dragColumn: -1,
	dragStyle: "services",
	dragWidth: 0,
	startX: 0,
	currentX: 0,
	dragItem: null,
	dragClass: "",
    init: function() {
		$(".dragger").off("mousedown", dragging.dragWhat);
		$(document).off("mouseup", dragging.dragOff);
		$(document).off("mousemove", dragging.dragging);
		$(".dragger").on("mousedown", dragging.dragWhat);
		$(document).on("mouseup", dragging.dragOff);
		$(document).on("mousemove", dragging.dragging);
    },
	dragging: function(e) {
		if (dragging.dragColumn>0) {
			dragging.currentX = e.clientX;
			var difference = dragging.startX - dragging.currentX;
			var size = dragging.dragWidth+(difference);
			//if (size>40) {
				var items = dragging.dragStyle.split(' ');
				var newStyle = "";
				for (var i=0; i<items.length; i++) {
					if (i==1) newStyle += "auto ";
					else if (i==dragging.dragColumn) newStyle += size+"px ";
					else if (i>1 && i==dragging.dragColumn-1) newStyle += (Number(items[i].split('px').join(''))-difference)+"px ";
					else newStyle += items[i]+" ";
				}
				$(".services").css("grid-template-columns", newStyle);
			//}
		}
	},
	dragWhat: function(e) {
		var dragger = $(e.currentTarget);
		var pos = dragger.parent().index();
		dragging.dragStyle = dragger.parent().parent().css("grid-template-columns");
		dragging.dragClass = dragger.parent().parent().attr('class');
		dragging.dragColumn = pos;
		dragging.dragItem = dragger.parent();
		dragging.dragWidth = dragger.parent().width();
		dragging.startX = e.clientX;
		dragging.currentX = e.clientX;
	},
	dragOff: function() {
		dragging.dragColumn = -1;
	}
}