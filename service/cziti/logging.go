package cziti

import (
	"fmt"
	"github.com/robfig/cron/v3"
)

var c = cron.New()

func Bob() {
	c.AddFunc("@midnight", func() { fmt.Println("Every hour on the half hour") })
	c.AddFunc("@every 1s", everySecond)
	c.Start()
}

func everySecond() {
	fmt.Println("ticking a second")
}

/*
uv_async_t async;
uv_async_init(loop, &async, my_callback);
async.data = "hi";

uv_async_send(&async);
*/