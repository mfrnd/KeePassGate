using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>Decides whether a form that KeePass just opened is one this plugin decorates.</summary>
    public interface IFormMatcher
    {
        bool Matches(Form form);
    }

    /// <summary>
    /// Matches on the full type name. A string rather than a type reference, so that everything
    /// above the KeePass boundary stays compilable and testable without KeePass loaded, and so
    /// that a KeePass version which drops the type fails as "no match" rather than as a
    /// <see cref="TypeLoadException"/> during plugin load.
    /// </summary>
    public sealed class TypeNameFormMatcher : IFormMatcher
    {
        private readonly string _typeName;

        public TypeNameFormMatcher(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("A type name is required.", nameof(typeName));
            }

            _typeName = typeName;
        }

        public bool Matches(Form form)
        {
            if (form == null)
            {
                return false;
            }

            return string.Equals(form.GetType().FullName, _typeName, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return _typeName;
        }
    }

    /// <summary>
    /// Matches a form that any of several matchers recognizes.
    /// </summary>
    /// <remarks>
    /// The grant editor goes on two dialogs that have nothing in common but the tab it puts there,
    /// and one decorator watching for either is the arrangement that cannot end up attaching a
    /// different tab to each by accident.
    /// </remarks>
    public sealed class AnyFormMatcher : IFormMatcher
    {
        private readonly IFormMatcher[] _matchers;

        public AnyFormMatcher(params IFormMatcher[] matchers)
        {
            _matchers = matchers ?? Array.Empty<IFormMatcher>();
        }

        public bool Matches(Form form)
        {
            foreach (var matcher in _matchers)
            {
                if (matcher != null && matcher.Matches(form))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
        {
            return string.Join(" or ", Array.ConvertAll(_matchers, m => m == null ? "(nothing)" : m.ToString()));
        }
    }
}
