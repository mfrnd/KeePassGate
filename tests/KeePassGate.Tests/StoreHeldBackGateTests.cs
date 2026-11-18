using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A database whose store holds the gate back, over a real socket and a real handshake,
    /// `STORE.md` §656.
    /// </summary>
    /// <remarks>
    /// Answered with the served authority's own host certificate, before a route is chosen and
    /// with nothing of the request read, in the body's `error` and in `KPG-Unavailable`, as
    /// `undecided` is.
    /// </remarks>
    [TestFixture]
    public sealed class StoreHeldBackGateTests
    {
        private TestAuthority _authority;
        private RecordingGateLog _log;
        private GateListener _listener;
        private CountingHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _log = new RecordingGateLog();
            _handler = new CountingHandler();
            _listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(new FakeGateTargets(_authority), _log),
                _handler,
                _log);

            Assert.That(_listener.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _listener.Dispose();
            _authority.Dispose();
        }

        [TestCase("stopped", "GET")]
        [TestCase("stopped", "HEAD")]
        [TestCase("split", "GET")]
        [TestCase("outdated", "HEAD")]
        [TestCase("lost", "GET")]
        [TestCase("lost", "HEAD")]
        public void A_request_is_answered_the_store_s_word_before_a_route_is_chosen(string word, string method)
        {
            _authority.Unavailable = word;

            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var reply = GateClient.Send(
                    _listener.Port,
                    _authority.NewName(),
                    certificate,
                    method + " /entries HTTP/1.1\r\nHost: gate\r\n\r\n");

                Assert.That(reply.Status, Is.EqualTo(503), reply.Failure);
                Assert.That(reply.Raw, Does.Contain(HttpResponse.UnavailableField + ": " + word + "\r\n"));
                if (method == "GET")
                {
                    Assert.That(reply.Body, Does.Contain("\"" + word + "\""));
                }

                Assert.That(_handler.Calls, Is.Zero, "no route was chosen and nothing of the request was read");
            }
        }

        private sealed class CountingHandler : IGateHandler
        {
            public int Calls { get; private set; }

            public void Handle(IGateContext context)
            {
                Calls++;
                context.Respond(200, "{}");
            }
        }
    }
}
