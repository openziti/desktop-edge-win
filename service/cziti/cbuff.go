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
