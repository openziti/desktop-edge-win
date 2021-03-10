import sys
import socket

def port_check(HOST_PORT, TIMEOUT):
   t = HOST_PORT.split(':')
   HOST = t[0]
   PORT = t[1]
   s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
   try:
      s.settimeout(int(TIMEOUT))
      s.connect((HOST, int(PORT)))
      s.shutdown(2)
      print "Probing :", HOST, ": success"
      return True
   except:
      print "Probing: ", HOST, ": fail after", TIMEOUT, "seconds"
      return False

port_check(sys.argv[1], sys.argv[2])
