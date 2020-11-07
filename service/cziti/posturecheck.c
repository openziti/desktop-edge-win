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

void rreturnDomain(ziti_context ztx, char* id, ziti_pr_domain_cb response_cb, char* domain) {
    response_cb(ztx, id, domain);
}
void rreturnMacs(ziti_context ztx, char* id, ziti_pr_mac_cb response_cb, char** mac_addresses, int num_mac) {
    response_cb(ztx, id, mac_addresses, num_mac);
}
void rreturnOsInfo(ziti_context ztx, char* id, ziti_pr_os_cb response_cb, char* os_type, char* os_version, char* os_build) {
    response_cb(ztx, id, os_type, os_version, os_build);
}
void rreturnProcInfo(ziti_context ztx, char* id, char* path, ziti_pr_process_cb response_cb, bool is_running, char* sha, char** signers, int num_signers) {
    response_cb(ztx, id, path, is_running, sha, signers, num_signers);
}

char** makeCharArray(int size) {
    return calloc(sizeof(char*), size);
}

void setArrayString(char **a, char *s, int n) {
    a[n] = s;
}

void freeCharArray(char **a, int size) {
    int i;
    for (i = 0; i < size; i++) {
        free(a[i]);
    }
    free(a);
}