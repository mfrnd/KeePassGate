using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The profiles page in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// The row that makes a profile is fixed, so what varies is the table under it: nothing in it,
    /// counts that disagree with each other, and more names than fit. The counts are the point of
    /// the page, and the state worth looking hardest at is the one where they differ, because a
    /// name with rules and no clients and a name with clients and no rules are the two things the
    /// two columns exist to tell apart.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class ProfileListGallery
    {
        [Test]
        public void Screenshot()
        {
            // Nothing named. The page has to say where a profile comes from, because the table
            // says nothing at all.
            Shoot("profiles-01-empty", Gallery.SettingsPage, Named());

            // The ordinary case: a name that is granted something and held by somebody.
            Shoot("profiles-02-one", Gallery.SettingsPage, Named(Profile("reader", 2, 1)));

            // The two counts disagreeing, in both directions, which is what they are for.
            Shoot(
                "profiles-03-counts-that-differ",
                Gallery.SettingsPage,
                Named(
                    Profile("reader", 3, 2),
                    Profile("written-ahead", 4, 0),
                    Profile("held-and-granted-nothing", 0, 1),
                    Profile("neither", 0, 0)));

            // Counts wide enough to test a column sized to its heading rather than its cells.
            Shoot(
                "profiles-04-large-counts",
                Gallery.SettingsPage,
                Named(Profile("reader", 128, 64), Profile("ops", 9999, 1024)));

            // More than fits.
            Shoot("profiles-05-scrolling", Gallery.SettingsPage, Named(Crowd(12)));

            // A name at the length the issuer still allows.
            Shoot(
                "profiles-06-long-name",
                Gallery.SettingsPage,
                Named(Profile("a-profile-name-that-somebody-really-did-type-out-in-full", 1, 1)));
        }

        [Test]
        public void ScreenshotAfterAGesture()
        {
            // What the page says once something has happened, which is a band it does not otherwise
            // have.
            var profiles = Named(Profile("reader", 1, 0));

            using (var list = new ProfileList(profiles, null))
            {
                list.Remove(0);
                Gallery.Settle();
                Gallery.Shot(list, Gallery.SettingsPage, "profiles-07-after-a-removal");
            }
        }

        private static void Shoot(string name, Size size, FixedProfiles profiles)
        {
            using (var list = new ProfileList(profiles, null))
            {
                Gallery.Shot(list, size, name);
            }
        }

        private static Counted Profile(string name, int rules, int clients)
        {
            return new Counted { Name = name, Rules = rules, Clients = clients };
        }

        private static Counted[] Crowd(int count)
        {
            var many = new List<Counted>();

            for (var i = 0; i < count; i++)
            {
                many.Add(Profile(
                    "profile-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                    i,
                    i % 3));
            }

            return many.ToArray();
        }

        private static FixedProfiles Named(params Counted[] profiles)
        {
            return new FixedProfiles(profiles);
        }

        /// <summary>What one row of the table is, without a database behind it.</summary>
        private sealed class Counted
        {
            public string Name { get; set; }

            public int Rules { get; set; }

            public int Clients { get; set; }
        }

        /// <summary>A set of profiles with counts written down rather than counted.</summary>
        private sealed class FixedProfiles : IProfiles
        {
            private readonly List<Counted> _profiles;

            public FixedProfiles(IEnumerable<Counted> profiles)
            {
                _profiles = new List<Counted>(profiles);
            }

            /// <summary>
            /// The longest thing a removal says: what the name does now, the rules left waiting,
            /// and the clients still holding it.
            /// </summary>
            public string LastRemoval
            {
                get
                {
                    return "The profile reader is gone, so it grants nothing. The 12 rules naming it "
                        + "stay, inert. 12 clients still hold it.";
                }
            }

            /// <summary>Nothing: the pictures here are of the page, not of the question it asks.</summary>
            public string Returning(string name)
            {
                return null;
            }

            public IReadOnlyList<string> Names()
            {
                return _profiles.ConvertAll(p => p.Name);
            }

            public IReadOnlyDictionary<string, int> Rules()
            {
                var counts = new Dictionary<string, int>();
                _profiles.ForEach(p => counts[p.Name] = p.Rules);
                return counts;
            }

            public IReadOnlyDictionary<string, int> Clients()
            {
                var counts = new Dictionary<string, int>();
                _profiles.ForEach(p => counts[p.Name] = p.Clients);
                return counts;
            }

            public string Add(string name)
            {
                _profiles.Add(new Counted { Name = name });
                return null;
            }

            public string Remove(string name)
            {
                _profiles.RemoveAll(p => p.Name == name);
                return null;
            }
        }
    }
}
