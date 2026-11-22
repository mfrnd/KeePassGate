"""How long the tests wait on a peer that answers from a thread of the test process itself.

The stand-in gate, and every peer a test writes for itself, serve from threads of the process the
tests run in. A wait on any of them is a wait for that process to be given a processor, and on a
machine busy with other work, as a hosted runner is while it builds and tests the plugin beside
these tests, a handshake that takes milliseconds alone can wait seconds, with nothing wrong at
either end. So every such wait is given this figure, on the client's side and on the stand-in's,
and a test about a timeout gives its own, short and explicit, beside what it asserts.
"""

#: Seconds, for every wait a test makes on a peer in its own process: the `connection_timeout` of
#: each client the tests build to reach one, and the `quiet` of each stand-in gate. Long enough that
#: a test process kept off the processor for a while still hears its own peer, and short enough that
#: a peer that really hangs fails its test within a minute. Distinct from the client's default, so a
#: test can still tell the two apart.
PEER_TIMEOUT = 60.0
