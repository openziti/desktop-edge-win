
#include "sdk.h"
#include <nf/ziti_tunneler.h>
#include <nf/ziti_log.h>

void libuv_stopper(uv_async_t *a) {
     uv_stop(a->loop);
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
