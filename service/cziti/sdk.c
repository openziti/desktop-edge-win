/*
 * Copyright NetFoundry, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

#include "sdk.h"
#include <ziti/ziti_tunnel.h>
#include <ziti/ziti_log.h>

void libuv_stopper(uv_async_t *a) {
     uv_stop(a->loop);
}
void setLogLevel(int level) {
    ziti_debug_level = level;
}

void setLogOut(intptr_t h) {
    int fd = _open_osfhandle(h, _O_APPEND | _O_RDONLY);

    if (fd == -1) {
       fprintf(stderr, "failed to get fd from handle: %d\n", errno);
       fflush(stderr);
       return;
    }

    FILE *f = _fdopen(fd, "a+");
    if (f == NULL) {
        fprintf(stderr, "setLogOut: %d\n", errno);
        fflush(stderr);
        return;
    }

    ziti_set_log(f);
}

void libuv_init(libuv_ctx *lctx) {
    init_debug();
    lctx->l = uv_default_loop();
    uv_async_init(lctx->l, &lctx->stopper, libuv_stopper);
}

void libuv_runner(void *arg) {
    ZITI_LOG(INFO, "starting event loop");
    uv_loop_t *l = arg;
    uv_run(l, UV_RUN_DEFAULT);

    ZITI_LOG(INFO, "event finished finished\n");
}

void libuv_run(libuv_ctx *lctx) {
    uv_thread_create(&lctx->t, libuv_runner, lctx->l);
}

void libuv_stop(libuv_ctx *lctx) {
    uv_async_send(&lctx->stopper);
    int err = uv_thread_join(&lctx->t);
    if (err) {
        ZITI_LOG(ERROR, "failed to join uv_loop thread: %d[%s]", err, uv_strerror(err));
    }
}

static const char* _all[] = {
   "all", NULL
};

const char** all_configs = _all;

extern void call_on_packet(const char *packet, ssize_t len, packet_cb cb, void *ctx) {
     cb(packet, len, ctx);
}

extern void c_mapiter(model_map *map) {
    int rc = 0;
	model_map_iter it = model_map_iterator(map);
	while (it != NULL && rc == 0) {
		char *v = model_map_it_value(it);
		const char *k = model_map_it_key(it);
		printf("k: %s, v: %s", k, v);
		it = model_map_it_next(it);
	}
}

void ZLOG(int level, char* msg) {
    ZITI_LOG(level, "%s", msg);
}