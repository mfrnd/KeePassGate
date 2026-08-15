using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A complete, disposable KeePass: its own copy of the program, its own configuration, its own
    /// database, and the plugin under test in its plugins directory.
    /// </summary>
    /// <remarks>
    /// Nothing here touches the developer's KeePass or the developer's database. The copy exists
    /// so that the test cannot inherit a language, a policy or a setting from a real installation,
    /// and so that a failed run leaves nothing behind but one directory under
    /// `CommonApplicationData`. Not `%TEMP%`, which carries the account name: the pictures the
    /// documentation is built from show the open database's path, so where the session lives is
    /// what makes them publishable.
    /// </remarks>
    internal sealed class ThrowawayKeePass : IDisposable
    {
        /// <summary>Invented, and only ever passed on the command line of a throwaway database.</summary>
        public const string DatabasePassword = "not-a-real-password-9c1f";

        private static readonly string[] RequiredFiles =
        {
            "KeePass.exe",
            "KeePass.exe.config"
        };

        /// <summary>Present in a normal installation, and KeePass starts without any of them.</summary>
        private static readonly string[] OptionalFiles =
        {
            "KeePass.XmlSerializers.dll",
            "KeePassLibN.x32.dll",
            "KeePassLibN.x64.dll",
            "KeePassLibN.a64.dll"
        };

        private readonly string _root;
        private readonly int _port;
        private TcpListener _reservation;
        private Process _process;

        private ThrowawayKeePass(string root, TcpListener reservation)
        {
            _root = root;
            _reservation = reservation;
            _port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        }

        /// <summary>
        /// The port this KeePass is told to listen on: the documented one when nothing holds it,
        /// and anything free when something does, so that what is published does not change for a
        /// reason nobody chose and a test still never collides with a real instance.
        /// </summary>
        public int Port
        {
            get { return _port; }
        }

        public string ExePath
        {
            get { return Path.Combine(_root, "KeePass.exe"); }
        }

        public string DatabasePath
        {
            get { return Path.Combine(_root, "throwaway.kdbx"); }
        }

        public string PluginLogPath
        {
            get { return Path.Combine(_root, "keepassgate.log"); }
        }

        public Process Process
        {
            get { return _process; }
        }

        /// <summary>
        /// Builds the installation. The KeePass to copy is the one this test assembly was built
        /// against, so the live test and the in process tests can never drift onto two builds.
        /// </summary>
        public static ThrowawayKeePass Create(string workingDirectory)
        {
            return Create(workingDirectory, false);
        }

        /// <summary>
        /// Builds the installation, optionally with an authority already in the database, which is
        /// what a test needs when it is the gate it wants to reach rather than the tab.
        /// </summary>
        public static ThrowawayKeePass Create(string workingDirectory, bool withAuthority)
        {
            // No identifier here: the working directory this sits in is already one per session.
            var root = Path.Combine(workingDirectory, "keepass");
            Directory.CreateDirectory(root);

            var source = KeePassDev.Directory;
            foreach (var name in RequiredFiles)
            {
                var from = Path.Combine(source, name);
                if (!File.Exists(from))
                {
                    throw new FileNotFoundException("The test copy of KeePass is incomplete.", from);
                }

                File.Copy(from, Path.Combine(root, name));
            }

            foreach (var name in OptionalFiles)
            {
                var from = Path.Combine(source, name);
                if (File.Exists(from))
                {
                    File.Copy(from, Path.Combine(root, name));
                }
            }

            WriteConfiguration(root);

            // Given back if anything below throws. Nothing else holds the instance until it is
            // returned, so a failure here left the reserved port bound until a collection found
            // the socket, and the next fixture in the same run quietly took another port.
            var installation = new ThrowawayKeePass(root, Reserve());
            var built = false;
            try
            {
                installation.InstallPlugin();
                installation.CreateDatabase(withAuthority);
                built = true;
                return installation;
            }
            finally
            {
                if (!built)
                {
                    installation.Dispose();
                }
            }
        }

        public Process Start()
        {
            var info = new ProcessStartInfo
            {
                FileName = ExePath,
                // The password reaches the process list here. That is only ever acceptable because
                // this database is invented, empty and deleted at the end of the test.
                Arguments = "\"" + DatabasePath + "\" -pw:" + DatabasePassword,
                WorkingDirectory = _root,
                UseShellExecute = false
            };
            info.EnvironmentVariables[TraceGateLog.LogFileVariable] = PluginLogPath;
            info.EnvironmentVariables[KeePassGate.Transport.GateOptions.PortVariable] =
                _port.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // The one before it is dealt with first. This fixture is started twice, once to
            // create the authority and once to read it back after a restart, and simply
            // overwriting the field orphaned the first KeePass whenever it had failed to
            // exit. That orphan holds the database and the copied KeePass open, so both
            // deletes in `Dispose` fail, and its window then swallows the keystrokes the
            // next fixture sends: one real failure turned into a run of unrelated ones.
            Release();

            // The port goes back a moment before the process that wants it is launched, rather
            // than when it was chosen. Nothing else on the machine gets a window to take it.
            DropReservation();

            _process = Process.Start(info);
            return _process;
        }

        /// <summary>Gives the reserved port up. Safe to call when there is nothing to give up.</summary>
        private void DropReservation()
        {
            var reserved = _reservation;
            _reservation = null;

            if (reserved == null)
            {
                return;
            }

            try
            {
                reserved.Stop();
            }
            catch (SocketException)
            {
            }
        }

        public string ReadPluginLog()
        {
            if (!File.Exists(PluginLogPath))
            {
                return string.Empty;
            }

            using (var stream = new FileStream(PluginLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Kills whatever this fixture last started, and lets go of it.
        /// </summary>
        /// <remarks>
        /// Its own method because `Start` needs it as much as `Dispose` does: the field
        /// holds one process, and starting a second without ending the first leaves a
        /// KeePass nothing will ever close.
        /// </remarks>
        private void Release()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(10000);
                }
            }
            catch (Exception error) when (
                error is InvalidOperationException || error is Win32Exception)
            {
                // Already gone, or going. Either way there is nothing left to kill.
            }
            finally
            {
                if (_process != null)
                {
                    _process.Dispose();
                    _process = null;
                }
            }
        }

        /// <summary>
        /// Writes a fixed authority and one fixed admitted client into the file.
        /// </summary>
        /// <remarks>
        /// **Written rather than minted, because a minted one is different every time.** The plugin
        /// signs with .NET's `CertificateRequest`, whose ECDSA picks a random k, so the same
        /// certificate made twice has different bytes and a different fingerprint. Any picture
        /// taken of this database shows that fingerprint and the dates beside it, so a
        /// database that mints its own is a database that changes the pictures on every
        /// regeneration.
        ///
        /// **Only before KeePass starts**, for the reason `Admit` gives below: a running KeePass
        /// serves the database it holds in memory and would overwrite this on its next save.
        /// </remarks>
        public void SeedAuthority()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                // Through `CustomDataStore`, not the dictionary underneath it, so that a seeded
                // database is written exactly the way the plugin writes one.
                var store = new CustomDataStore(database);
                string fingerprint;
                using (var authority = CertificateBlob.Load(SeededAuthority.Authority))
                {
                    fingerprint = CertificateDigest.Of(authority);
                }

                store.Set(
                    AuthorityKeys.AuthorityKey(fingerprint),
                    AuthorityDocument.Save(
                        SeededAuthority.Authority, SeededAuthority.Host, SeededAuthority.SniKey));
                store.Set(SeededAuthority.ClientKey, SeededAuthority.ClientRecord);

                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Ends this KeePass without ending the session it belongs to, so another can be
        /// started against the same file.
        /// </summary>
        /// <remarks>
        /// Delegates rather than repeats. The first version of this was a copy of `Release`
        /// with `Win32Exception` dropped from the filter, which is exactly what `Kill` throws
        /// when the target is already terminating.
        /// </remarks>
        public void Stop()
        {
            Release();
        }

        public void Dispose()
        {
            DropReservation();
            Release();

            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// A local configuration beside the program is what makes KeePass ignore the developer's
        /// own settings, including any language they have selected. It answers four things.
        /// </summary>
        /// <remarks>
        /// The update question is asked on a first start and blocks behind a modal dialog before
        /// any plugin is loaded. Single instance handling matters more: with it left on, a KeePass
        /// that is already running takes over the command line and this process exits at once, so
        /// the test would drive somebody else's KeePass or nothing at all. The sections follow the
        /// order KeePass declares them in, which its own serializer does not require: measured on
        /// 2.61.1, a section and a setting out of that order are read alike. What it does not do
        /// is say anything about a setting it did not take, which is what
        /// `ThrowawayConfigurationTests` is for.
        ///
        /// **And Escape does nothing to the main window.** KeePass 2.61.1 locks the whole
        /// workspace on an Escape that reaches its main window with no menu open, which is its
        /// default. A test sends Escape to close a dialog or a menu, and one that arrives after
        /// the thing it was for has gone, or when a menu did not open, would lock the database
        /// every later step works on, and a failure about one menu would then read as a run of
        /// failures about something else.
        /// </remarks>
        private static void WriteConfiguration(string root)
        {
            File.WriteAllText(Path.Combine(root, "KeePass.config.xml"), Configuration, new UTF8Encoding(false));
        }

        /// <summary>
        /// What <see cref="WriteConfiguration"/> writes, where a test can read it the way KeePass
        /// does.
        /// </summary>
        internal const string Configuration =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<Configuration>\r\n" +
                "  <Application>\r\n" +
                "    <Start>\r\n" +
                "      <CheckForUpdate>false</CheckForUpdate>\r\n" +
                "      <CheckForUpdateConfigured>true</CheckForUpdateConfigured>\r\n" +
                "    </Start>\r\n" +
                "  </Application>\r\n" +
                "  <MainWindow>\r\n" +
                "    <EscAction>None</EscAction>\r\n" +
                "  </MainWindow>\r\n" +
                "  <Integration>\r\n" +
                "    <LimitToSingleInstance>false</LimitToSingleInstance>\r\n" +
                "  </Integration>\r\n" +
                "</Configuration>\r\n";

        private void InstallPlugin()
        {
            var pluginAssembly = typeof(KeePassGateExt).Assembly;
            var pluginPath = new Uri(pluginAssembly.CodeBase).LocalPath;
            // In a directory of its own under `plugins`, which is where `build.ps1 -Task Deploy`
            // and the operator's documentation put it, so that the live tier loads the layout an
            // installation has rather than one nobody ships.
            var plugins = Path.Combine(_root, "plugins", "KeePassGate");
            Directory.CreateDirectory(plugins);
            File.Copy(pluginPath, Path.Combine(plugins, Path.GetFileName(pluginPath)));

            // And what it needs, which is how the live test proves that a plugin with dependencies
            // actually loads: KeePass uses Assembly.LoadFrom, which probes the plugin's own
            // directory.
            //
            // **What it needs, followed through, rather than everything beside it.** The directory
            // this runs from is a test host's: it also holds NUnit, the test adapter and
            // `System.Runtime.CompilerServices.Unsafe`, which is the one assembly the plugin may
            // never ship because it wants a binding redirect only KeePass's own configuration could
            // give it. Copying the directory put all of that beside the plugin, so the live tier
            // proved something deployment never produces.
            //
            // **Followed through, and not one level deep.** A dependency that brings one of its own
            // is not named by the plugin at all, so what each assembly references is followed in
            // turn. A plugin missing a transitive dependency fails to load with nothing in the log
            // to say why.
            var beside = Path.GetDirectoryName(pluginPath);
            foreach (var dependency in NeededBy(pluginAssembly, beside))
            {
                File.Copy(dependency, Path.Combine(plugins, Path.GetFileName(dependency)), true);
            }

            // And their notices, which each of their licenses asks to go with every copy, and which
            // `build.ps1 -Task Deploy` puts beside them as well.
            var notices = Path.Combine(Repository.Root, "THIRD-PARTY-NOTICES.md");
            File.Copy(notices, Path.Combine(plugins, Path.GetFileName(notices)));

            var symbols = Path.ChangeExtension(pluginPath, ".pdb");
            if (File.Exists(symbols))
            {
                File.Copy(symbols, Path.Combine(plugins, Path.GetFileName(symbols)));
            }
        }

        /// <summary>
        /// Every assembly the plugin references that is found beside it, and whatever those
        /// reference in turn.
        /// </summary>
        /// <remarks>
        /// Followed transitively rather than taken one level deep, because a dependency that brings
        /// one of its own is not named by the plugin at all and a plugin missing one fails to load
        /// with nothing in the log to say why. Anything not found beside the plugin is the
        /// framework or KeePass, both of which the host already has.
        /// </remarks>
        private static IEnumerable<string> NeededBy(Assembly plugin, string beside)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<Assembly>();
            pending.Enqueue(plugin);

            while (pending.Count > 0)
            {
                foreach (var referenced in pending.Dequeue().GetReferencedAssemblies())
                {
                    if (!seen.Add(referenced.Name))
                    {
                        continue;
                    }

                    var path = Path.Combine(beside, referenced.Name + ".dll");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    found.Add(path);

                    try
                    {
                        pending.Enqueue(Assembly.ReflectionOnlyLoadFrom(path));
                    }
                    catch (BadImageFormatException)
                    {
                        // Not managed, so it names nothing further. Copied anyway.
                    }
                    catch (FileLoadException)
                    {
                        // Already loaded for reflection under another path. Its references are
                        // reachable from the copy that is, so nothing is lost by stopping here.
                    }
                }
            }

            return found;
        }

        private void CreateDatabase(bool withAuthority)
        {
            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath(DatabasePath), NewKey());
            database.Name = "KeePassGate throwaway";
            database.RootGroup.Name = "Throwaway";

            if (withAuthority)
            {
                var created = new AuthorityService(
                    new CustomDataStore(database), NullGateLog.Instance).Create();
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException("The throwaway database got no authority: " + created.Message);
                }
            }

            database.Save(null);
            database.Close();
        }

        /// <summary>
        /// Opens the database the way anybody holding the file could, which is how a test issues
        /// itself a client certificate and works out a name the gate will answer to.
        /// </summary>
        public AuthorityReading ReadAuthority()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                return new AuthorityStore(new CustomDataStore(database), NullGateLog.Instance).Read();
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>A name this database's gate will answer to, fresh each time.</summary>
        public string NewGateName()
        {
            using (var reading = ReadAuthority())
            {
                return SniName.For(reading.Material.SniKey);
            }
        }

        /// <summary>
        /// A client certificate issued by the database's own authority, loaded so that TLS can
        /// present it. Pairing issues one the same way, from inside the plugin.
        /// </summary>
        public X509Certificate2 IssueClientCertificate(string subject)
        {
            using (var reading = ReadAuthority())
            using (var issued = new AuthorityIssuer().IssueClient(reading.Material.Authority, subject, null))
            using (KeePassGate.Testing.PrivateKeyFiles.Lock())
            {
                // With a key container, because the client in these tests is an SslStream and
                // SChannel will not present an ephemeral key. The plugin itself no longer asks for
                // one anywhere.
                return CertificateBlob.Load(
                    CertificateBlob.Save(issued), System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);
            }
        }

        /// <summary>
        /// Writes an admission record into the file, which is what pairing does from inside the
        /// plugin, through `AdmissionWriter.Write`.
        /// </summary>
        /// <remarks>
        /// **Only before KeePass starts.** A running KeePass holds the database in memory and
        /// serves from that, so a record written to the file underneath it is not seen, and would
        /// be overwritten by its next save. That is not a limitation to work around: it is why
        /// pairing writes through the plugin rather than through the file.
        /// </remarks>
        public void Admit(X509Certificate2 certificate, params string[] profiles)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                new AdmissionStore(new CustomDataStore(database), NullGateLog.Instance)
                    .Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles));

                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// The admission records in the file, read after KeePass has exited. This is the only way to
        /// find out whether pairing really made one durable, because everything KeePass reports
        /// about a save it reports to itself.
        /// </summary>
        public IReadOnlyList<AdmissionRecord> ReadAdmissions()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                return new AdmissionStore(
                    new CustomDataStore(database), NullGateLog.Instance).All().Records;
            }
            finally
            {
                database.Close();
            }
        }

        public string DatabaseId()
        {
            using (var reading = ReadAuthority())
            {
                return reading.Material.AuthorityDigest;
            }
        }

        private static CompositeKey NewKey()
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(DatabasePassword));
            return key;
        }

        /// <summary>The port examples and pictures use, or any free one.</summary>
        private const int Documented = 45678;

        /// <summary>A port nothing is using, held until the moment KeePass is told to take it.</summary>
        /// <remarks>
        /// **One port when it is free, rather than a fresh one every time.** Anything published or
        /// compared between runs then does not change for a reason nobody chose. Anything else free
        /// will do when something on the machine already holds it.
        ///
        /// **Held rather than let go.** Choosing it in `Create` and binding it in `Start` leaves
        /// seconds of copying and key generation in between, and the number is a fixed one, so a
        /// previous run's KeePass that has not finished exiting is the likeliest thing to take it.
        /// The reservation is dropped in `Start`, immediately before the process that wants it is
        /// launched.
        /// </remarks>
        private static TcpListener Reserve()
        {
            foreach (var port in new[] { Documented, 0 })
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                    return listener;
                }
                catch (SocketException)
                {
                    // The documented one is taken. Anything free will do.
                }
            }

            throw new InvalidOperationException("No loopback port could be reserved for the gate.");
        }

    }
}
