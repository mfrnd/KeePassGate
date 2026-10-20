using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;

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
        private Process _process;

        private ThrowawayKeePass(string root)
        {
            _root = root;
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

            var installation = new ThrowawayKeePass(root);
            installation.InstallPlugin();
            installation.CreateDatabase();
            return installation;
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
            info.EnvironmentVariables[KeePassGate.Diagnostics.TraceGateLog.LogFileVariable] = PluginLogPath;

            // The one before it is dealt with first. This fixture is started twice, once to
            // create the authority and once to read it back after a restart, and simply
            // overwriting the field orphaned the first KeePass whenever it had failed to
            // exit. That orphan holds the database and the copied KeePass open, so both
            // deletes in `Dispose` fail, and its window then swallows the keystrokes the
            // next fixture sends: one real failure turned into a run of unrelated ones.
            Release();

            _process = Process.Start(info);
            return _process;
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

        public void Dispose()
        {
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

        private void CreateDatabase()
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(DatabasePassword));

            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath(DatabasePath), key);
            database.Name = "KeePassGate throwaway";
            database.RootGroup.Name = "Throwaway";
            database.Save(null);
            database.Close();
        }
    }
}
