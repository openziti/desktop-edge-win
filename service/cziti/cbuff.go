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

package cziti

var cbuff = make([][]byte, MaxDnsRequests)
var pos int32 = -1

func init() {
	initDnsMsgBuffer()
}

func initDnsMsgBuffer() {
	for i := 0; i < MaxDnsRequests; i++ {
		cbuff[i] = make([]byte, DnsMsgBufferSize)
	}
}

func nextBuffer() *[]byte {
	pos = pos + 1
	if pos == MaxDnsRequests {
		pos = 0
	}
	return &cbuff[pos]
}
