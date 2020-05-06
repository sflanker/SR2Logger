#! /usr/bin/python

import socket, struct, sys
from datetime import timedelta

s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.bind(("localhost", 2837))

TypeFormats = [
    "",     # null
    "d",    # double (float64)
    "?",    # bool
    "ddd"]  # Vector3d


class Packet:
    def __init__(self, data):
        self.data = data
        self.pos = 0

    def get(self, l):  # get l bytes
        self.pos += l
        return self.data[self.pos - l:self.pos]

    def read(self, fmt):  # read all formatted values
        v = self.readAll(fmt)
        if len(v) == 0: return None
        if len(v) == 1: return v[0]
        return v

    def readAll(self, fmt):  # read multiple formatted values
        return struct.unpack(fmt, self.get(struct.calcsize(fmt)))

    @property
    def more(self):  # is there more data?
        return self.pos < len(self.data)


def readPacket(dat):
    p = Packet(dat)

    messageType = p.read("B")

    print("---")
    if messageType == 1:
        print("sample:")
        timestamp = p.read("Q")
        print("  timestamp: {0}".format(timedelta(milliseconds=timestamp)))
        print("  variables:")
        while p.more:
            nameLen = p.read("I")
            name = p.get(nameLen).decode('utf-8')

            tp = p.read("B")
            if tp == 4:
                # Text
                textLen = p.read("I")
                val = "'{0}'".format(p.get(textLen).decode('utf-8').replace("\\", "\\\\").replace("'", "\\'"))
            else:
                structFormat = TypeFormats[tp]
                val = p.read(structFormat)

            print("    {0}: {1}".format(name, val))
    elif messageType == 2:
        messageLen = p.read("I")
        message = p.get(messageLen).decode('utf-8')
        print("log: >-")
        print("  {0}".format(message))
    else:
        raise Exception("Unexpected message type: " + str(messageType))

    sys.stdout.flush()


sys.stderr.write("Starting Client...\n")
while 1:
    d, a = s.recvfrom(2048)
    readPacket(d)
