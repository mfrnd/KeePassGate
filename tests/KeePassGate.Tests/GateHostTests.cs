using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateHostTests
    {
        private FakeWindowEventSource _windows;
        private RecordingFormDecorator _decorator;
        private RecordingGateLog _log;
        private GateHost _host;

        [SetUp]
        public void SetUp()
        {
            _windows = new FakeWindowEventSource();
            _decorator = new RecordingFormDecorator();
            _log = new RecordingGateLog();
            _host = new GateHost(_windows, new TypeNameFormMatcher(typeof(TestDialogForm).FullName), _decorator, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
        }

        [Test]
        public void A_missing_collaborator_is_refused_at_construction()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GateHost(null, new TypeNameFormMatcher("X"), _decorator, _log));
            Assert.Throws<ArgumentNullException>(() =>
                new GateHost(_windows, null, _decorator, _log));
            Assert.Throws<ArgumentNullException>(() =>
                new GateHost(_windows, new TypeNameFormMatcher("X"), null, _log));
        }

        [Test]
        public void A_missing_log_is_tolerated_because_it_is_not_load_bearing()
        {
            var host = new GateHost(_windows, new TypeNameFormMatcher("X"), _decorator, null);

            Assert.That(host.Start(), Is.True);
            Assert.DoesNotThrow(host.Stop);
        }

        [Test]
        public void Starting_subscribes_once_and_reports_running()
        {
            Assert.That(_host.Start(), Is.True);

            Assert.That(_host.IsRunning, Is.True);
            Assert.That(_windows.AddedSubscriberCount, Is.EqualTo(1));
            Assert.That(_windows.RemovedSubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void Starting_twice_subscribes_once()
        {
            _host.Start();
            _host.Start();

            Assert.That(_windows.AddedSubscriberCount, Is.EqualTo(1));

            using (var form = Dialogs.WithNestedTabControl())
            {
                _windows.RaiseWindowAdded(form);
                Assert.That(_decorator.Attached, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void A_matching_dialog_is_decorated_when_it_opens()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl())
            {
                _windows.RaiseWindowAdded(form);

                Assert.That(_decorator.Attached, Is.EqualTo(new[] { form }));
            }
        }

        [Test]
        public void Any_other_dialog_is_left_alone()
        {
            _host.Start();

            using (var other = new Form())
            {
                _windows.RaiseWindowAdded(other);
                _windows.RaiseWindowRemoved(other);

                Assert.That(_decorator.Attached, Is.Empty);
                Assert.That(_decorator.Detached, Is.Empty);
            }
        }

        [Test]
        public void A_matching_dialog_is_undecorated_when_it_closes()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl())
            {
                _windows.RaiseWindowAdded(form);
                _windows.RaiseWindowRemoved(form);

                Assert.That(_decorator.Detached, Is.EqualTo(new[] { form }));
            }
        }

        [Test]
        public void Stopping_unsubscribes_and_undecorates_everything()
        {
            _host.Start();

            _host.Stop();

            Assert.That(_host.IsRunning, Is.False);
            Assert.That(_windows.AddedSubscriberCount, Is.Zero);
            Assert.That(_windows.RemovedSubscriberCount, Is.Zero);
            Assert.That(_decorator.DetachAllCount, Is.EqualTo(1));
        }

        [Test]
        public void After_stopping_a_dialog_that_opens_anyway_is_ignored()
        {
            _host.Start();
            _host.Stop();

            using (var form = Dialogs.WithNestedTabControl())
            {
                _windows.RaiseWindowAdded(form);

                Assert.That(_decorator.Attached, Is.Empty);
            }
        }

        [Test]
        public void Stopping_without_starting_is_harmless()
        {
            Assert.DoesNotThrow(_host.Stop);

            Assert.That(_decorator.DetachAllCount, Is.Zero);
            Assert.That(_log.Errors, Is.Empty);
        }

        [Test]
        public void Stopping_twice_undecorates_once()
        {
            _host.Start();

            _host.Stop();
            _host.Stop();

            Assert.That(_decorator.DetachAllCount, Is.EqualTo(1));
        }

        [Test]
        public void Starting_again_after_stopping_works()
        {
            _host.Start();
            _host.Stop();

            Assert.That(_host.Start(), Is.True);
            Assert.That(_windows.AddedSubscriberCount, Is.EqualTo(1));

            using (var form = Dialogs.WithNestedTabControl())
            {
                _windows.RaiseWindowAdded(form);
                Assert.That(_decorator.Attached, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void A_decorator_that_throws_never_reaches_the_host_application()
        {
            _host.Start();
            _decorator.AttachThrows = new InvalidOperationException("attach exploded");

            using (var form = Dialogs.WithNestedTabControl())
            {
                Assert.DoesNotThrow(() => _windows.RaiseWindowAdded(form));

                // Reported as an incident rather than as an ordinary error. This is one of the
                // places that catch everything, so what it caught is a surprise by
                // definition, and the operator is told the count on the KPG tab.
                Assert.That(_log.Surprises, Is.Not.Empty);
            }
        }

        [Test]
        public void A_decorator_that_throws_while_stopping_never_reaches_the_host_application()
        {
            _host.Start();
            _decorator.DetachAllThrows = new InvalidOperationException("detach exploded");

            Assert.DoesNotThrow(_host.Stop);

            Assert.That(_host.IsRunning, Is.False);
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void An_event_carrying_no_form_is_ignored()
        {
            _host.Start();

            Assert.DoesNotThrow(() => _windows.RaiseWindowAdded(null));
            Assert.DoesNotThrow(() => _windows.RaiseWindowAddedRaw(null));

            Assert.That(_decorator.Attached, Is.Empty);
            Assert.That(_log.Errors, Is.Empty);
        }

        [Test]
        public void Disposing_stops()
        {
            _host.Start();

            _host.Dispose();

            Assert.That(_host.IsRunning, Is.False);
            Assert.That(_windows.AddedSubscriberCount, Is.Zero);
        }
    }
}
