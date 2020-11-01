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

#include <stdio.h>
#include <stdlib.h>
#define USING_ZITI_SHARED
#include <ziti/ziti.h>
#include <ziti/ziti_log.h>
#include <uv.h>


typedef struct cziti_ctx_s {
    ziti_options opts;
    ziti_context nf;
    uv_async_t async;
} cziti_ctx;

typedef struct libuv_ctx_s {
    uv_loop_t *l;
    uv_thread_t t;
    uv_async_t stopper;
} libuv_ctx;

void libuv_stopper(uv_async_t *a);
void libuv_init(libuv_ctx *lctx);
void libuv_runner(void *arg);
void libuv_run(libuv_ctx *lctx);
void libuv_stop(libuv_ctx *lctx);

void set_log_out(intptr_t h);
void set_log_level(int level);

extern const char** all_configs;