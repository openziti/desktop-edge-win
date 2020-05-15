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

package service

type topic struct {
	broadcast chan interface{}
	channels map[int]chan interface{}
	done chan bool
}

func newTopic() topic {
	return topic{
		broadcast: make(chan interface{}, 8),
		channels:  make(map[int]chan interface{}),
		done:      make(chan bool),
	}
}

func(t *topic) register(id int, c chan interface{}) {
	t.channels[id] = c
}

func(t *topic) unregister(id int) {
	delete(t.channels, id)
}

func(t *topic) shutdown() {
	t.done <- true
}

func(t *topic) run() {
	go func() {
		for {
			select {
			case msg := <-t.broadcast:
				for _, c := range t.channels {
					c <- msg
				}
				break
			case <-t.done:
				return
			}
		}
	}()
}
