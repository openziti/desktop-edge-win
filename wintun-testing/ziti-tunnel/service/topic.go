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
