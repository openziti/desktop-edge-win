
#include "sdk.h"
#include <nf/ziti_tunneler.h>
#include <nf/ziti_log.h>

void libuv_stopper(uv_async_t *a) {
     uv_stop(a->loop);
}

void libuv_init(libuv_ctx *lctx) {
    lctx->l = uv_default_loop();
    uv_async_init(lctx->l, &lctx->stopper, libuv_stopper);
}

void libuv_runner(void *arg) {
    init_debug();
    ZITI_LOG(INFO, "starting event loop");
    fflush(stdout);
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
