using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using KeePassGate.Diagnostics;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What happens to a failure nobody predicted.
    /// </summary>
    /// <remarks>
    /// Four places in the plugin catch every exception, because in those four an escaping one
    /// ends the password manager. The risk that comes with such a catch is that it turns a defect
    /// into a log line nobody reads, so this fixture is about the other half: that it is counted,
    /// and that the count reaches the one screen the operator already looks at.
    /// </remarks>
    [TestFixture]
    public sealed class GateIncidentsTests
    {
        private RecordingGateLog _log;
        private GateIncidents _incidents;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
            _incidents = new GateIncidents(_log);
        }

        [Test]
        public void Nothing_has_gone_wrong_until_something_does()
        {
            Assert.That(_incidents.Count, Is.Zero);
            Assert.That(_incidents.Summary, Is.Empty, "an empty tab line rather than a reassuring one");
            Assert.That(_incidents.Recent, Is.Empty);
        }

        [Test]
        public void One_is_counted_logged_and_summarized()
        {
            _incidents.Record("a connection failed", new InvalidOperationException("boom"));

            Assert.That(_incidents.Count, Is.EqualTo(1));
            Assert.That(_incidents.Summary, Is.EqualTo("One unexpected failure, see the log."));
            Assert.That(_log.Surprises.Count(), Is.EqualTo(1));
        }

        [Test]
        public void It_is_logged_at_its_own_level_rather_than_as_another_error()
        {
            _incidents.Record("a connection failed", new InvalidOperationException("boom"));

            Assert.That(_log.Errors, Is.Empty, "these are not ordinary errors and must be findable apart from them");
            Assert.That(_log.Surprises.Single().Message, Does.Contain("a connection failed"));
            Assert.That(_log.Surprises.Single().Error, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Several_are_counted_together()
        {
            _incidents.Record("one", new InvalidOperationException());
            _incidents.Record("two", new InvalidOperationException());

            Assert.That(_incidents.Summary, Is.EqualTo("2 unexpected failures, see the log."));
        }

        [Test]
        public void What_it_remembers_is_bounded_because_a_client_chooses_when_these_happen()
        {
            for (var i = 0; i < GateIncidents.Keep * 3; i++)
            {
                _incidents.Record("failure " + i, new InvalidOperationException());
            }

            Assert.That(_incidents.Recent.Count, Is.EqualTo(GateIncidents.Keep));
            Assert.That(_incidents.Count, Is.EqualTo(GateIncidents.Keep * 3), "the count is not bounded, the list is");
            Assert.That(_incidents.Recent[_incidents.Recent.Count - 1], Does.Contain("failure 29"),
                "the newest is kept, not the oldest");
        }

        [Test]
        public void What_it_remembers_names_the_place_and_the_kind_and_nothing_else()
        {
            _incidents.Record("a connection failed", new InvalidOperationException("a client's own words"));

            var only = _incidents.Recent.Single();

            Assert.That(only, Is.EqualTo("a connection failed: InvalidOperationException"));
            Assert.That(only, Does.Not.Contain("a client's own words"),
                "the tab is not the place for text an attacker chose");
        }

        [Test]
        public void Recording_survives_being_given_nothing()
        {
            Assert.DoesNotThrow(() => _incidents.Record(null, null));

            Assert.That(_incidents.Count, Is.EqualTo(1));
        }

        [Test]
        public void A_log_that_throws_does_not_defeat_the_catch_that_called_it()
        {
            // This is called from inside the three handlers that stand between a failure and the
            // end of KeePass. If it could throw, those handlers would not be handlers.
            var incidents = new GateIncidents(new ThrowingLog());

            Assert.DoesNotThrow(() => incidents.Record("a connection failed", new InvalidOperationException()));
            Assert.That(incidents.Count, Is.EqualTo(1), "it is still counted, whatever the log did");
        }

        [Test]
        public void Recording_from_several_threads_at_once_counts_every_one()
        {
            using (var start = new ManualResetEventSlim(false))
            {
                var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
                {
                    start.Wait();
                    for (var i = 0; i < 50; i++)
                    {
                        _incidents.Record("a connection failed", new InvalidOperationException());
                    }
                })).ToList();

                threads.ForEach(t => t.Start());
                start.Set();
                threads.ForEach(t => t.Join());

                Assert.That(_incidents.Count, Is.EqualTo(400));
                Assert.That(_incidents.Recent.Count, Is.EqualTo(GateIncidents.Keep));
            }
        }

        [Test]
        public void A_null_log_is_not_an_excuse_to_stop_counting()
        {
            var incidents = new GateIncidents(null);

            Assert.DoesNotThrow(() => incidents.Record("somewhere", new InvalidOperationException()));
            Assert.That(incidents.Count, Is.EqualTo(1));
        }

        private sealed class ThrowingLog : IGateLog
        {
            public void Info(string message)
            {
                throw new InvalidOperationException("this log is broken");
            }

            public void Warn(string message)
            {
                throw new InvalidOperationException("this log is broken");
            }

            public void Error(string message, Exception error)
            {
                throw new InvalidOperationException("this log is broken");
            }

            public void Unexpected(string message, Exception error)
            {
                throw new InvalidOperationException("this log is broken");
            }
        }
    }

    /// <summary>
    /// The lists that decide what each handler catches.
    /// </summary>
    /// <remarks>
    /// The important tests here are the refusals. A filter that quietly matched everything would
    /// leave the codebase exactly where it started: swallowing the failures worth seeing.
    /// </remarks>
    [TestFixture]
    public sealed class ExpectedTests
    {
        private static readonly Type Filters = typeof(GateIncidents).Assembly.GetType("KeePassGate.Diagnostics.Expected");

        [Test]
        public void Cryptography_covers_the_ways_key_material_goes_wrong()
        {
            Assert.That(Matches("OfCryptography", new CryptographicException()), Is.True);
            Assert.That(Matches("OfCryptography", new FormatException()), Is.True, "base64 that is not base64");
            Assert.That(Matches("OfCryptography", new ArgumentException()), Is.True);
        }

        [Test]
        public void Custom_data_covers_the_race_the_gate_actually_runs()
        {
            // The gate reads custom data on a connection thread while the user interface thread can
            // be writing to the same dictionary, and that is what an invalidated enumeration throws.
            Assert.That(Matches("OfCustomData", new InvalidOperationException()), Is.True);
        }

        [Test]
        public void Reflection_covers_a_KeePass_that_has_moved_something()
        {
            Assert.That(Matches("OfReflection", new TargetInvocationException(new Exception())), Is.True);
            Assert.That(Matches("OfReflection", new MissingMethodException()), Is.True);
            Assert.That(Matches("OfReflection", new TypeLoadException()), Is.True);
        }

        [Test]
        public void A_connection_covers_the_socket_the_stream_and_the_handshake()
        {
            Assert.That(Matches("OfConnection", new IOException()), Is.True);
            Assert.That(Matches("OfConnection", new SocketException()), Is.True);
            Assert.That(Matches("OfConnection", new ObjectDisposedException("stream")), Is.True);
        }

        [Test]
        public void A_connection_does_not_cover_a_call_made_out_of_order()
        {
            // On a connection this type is only ever this plugin's own mistake: the TLS library
            // raises whatever goes wrong inside a record as an alert, which is an `IOException`,
            // and keeps this one for being used out of order. Covered, a defect while a name was
            // resolved read as a connection that ended early, and nothing counted it.
            Assert.That(Matches("OfConnection", new InvalidOperationException()), Is.False);
        }

        [Test]
        public void A_connection_does_not_cover_an_assembly_that_will_not_load()
        {
            // Both derive from `IOException`, and nothing on a connection opens a file: either one
            // there is the runtime failing to load something this plugin needs. Covered, a broken
            // installation would fail every connection as a client that hung up, and nothing would
            // count it.
            Assert.That(Matches("OfConnection", new FileNotFoundException("gone", "Invented.Dependency")), Is.False);
            Assert.That(Matches("OfConnection", new FileLoadException("replaced", "Invented.Dependency")), Is.False);
        }

        [Test]
        public void Our_own_failures_are_expected_wherever_our_own_layers_are_called()
        {
            var ours = new AuthorityFailure("the authority could not be read");

            Assert.That(Matches("OfCryptography", ours), Is.True);
            Assert.That(Matches("OfCustomData", ours), Is.True);
            Assert.That(Matches("OfUserInterface", ours), Is.True);
            Assert.That(Matches("OfConnection", ours), Is.True);
        }

        [Test]
        public void Issuance_covers_an_authority_whose_own_dates_have_passed()
        {
            // `AuthorityIssuer` refuses one with an `InvalidOperationException`, and that is the
            // state of an old database rather than a defect here: a site that issues without naming
            // it reports somebody's ten year old file as a fault in this plugin.
            Assert.That(Matches("OfIssuance", new InvalidOperationException()), Is.True);
            Assert.That(Matches("OfIssuance", new CryptographicException()), Is.True,
                "and everything a stored authority can be unusable in");

            Assert.That(Matches("OfCryptography", new InvalidOperationException()), Is.False,
                "the list it widens still does not, because nothing else reading a key issues one");
        }

        [TestCaseSource(nameof(EveryFilter))]
        public void Nothing_matches_the_failures_that_mean_there_is_a_defect(string filter)
        {
            // The whole point of the exercise. A handler that swallowed these would turn a defect
            // here, or a machine in trouble, into a log line about something else entirely.
            Assert.That(Matches(filter, new NullReferenceException()), Is.False);
            Assert.That(Matches(filter, new IndexOutOfRangeException()), Is.False);
            Assert.That(Matches(filter, new OutOfMemoryException()), Is.False);
            Assert.That(Matches(filter, new StackOverflowException()), Is.False);
            Assert.That(Matches(filter, new AccessViolationException()), Is.False);
        }

        [Test]
        public void Reflection_does_not_cover_a_socket_and_a_socket_does_not_cover_reflection()
        {
            // Each list is meant to be the failures of one kind of call, not a superset that has
            // drifted into being a catch-all with extra steps.
            Assert.That(Matches("OfReflection", new SocketException()), Is.False);
            Assert.That(Matches("OfConnection", new MissingMethodException()), Is.False);
            Assert.That(Matches("OfCryptography", new SocketException()), Is.False);
        }

        /// <summary>
        /// Every filter `Expected` declares, found rather than listed.
        /// </summary>
        /// <remarks>
        /// A list written out here named six filters while `Expected` held eleven, so widening one
        /// of the other five to take a `NullReferenceException` passed. Found by reflection, a
        /// filter added later is asked the same question the day it arrives. None found is an
        /// error rather than nothing to ask.
        /// </remarks>
        private static string[] EveryFilter()
        {
            var names = Filters == null
                ? Array.Empty<string>()
                : Filters.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => method.ReturnType == typeof(bool) &&
                                     method.GetParameters().Length == 1 &&
                                     method.GetParameters()[0].ParameterType == typeof(Exception))
                    .Select(method => method.Name)
                    .ToArray();

            if (names.Length == 0)
            {
                throw new InvalidOperationException("No filter was found on Expected, so nothing would be asked.");
            }

            return names;
        }

        private static bool Matches(string filter, Exception error)
        {
            Assert.That(Filters, Is.Not.Null, "the filters have moved");

            var method = Filters.GetMethod(filter, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "no filter called " + filter);

            return (bool)method.Invoke(null, new object[] { error });
        }
    }
}
