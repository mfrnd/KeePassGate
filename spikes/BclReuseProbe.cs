using System;
using System.Globalization;
using System.Net;
using System.Net.Mime;

/// <summary>
/// What the framework will do for this transport, and what it will do to it.
///
/// The three vendoring proofs asked whether somebody else's HTTP server could be taken. This asks
/// the smaller question underneath it: now that the transport is written here, which of its pieces
/// are already in an assembly the plugin loads anyway, so that less of it has to be ours?
///
/// `System` is already referenced -- `WebHeaderCollection` and `ContentType` live in the same
/// assembly as `Uri`, so none of this is a dependency, only a decision.
/// </summary>
public static class BclReuseProbe
{
    public static void Run()
    {
        Console.WriteLine("UI culture: " + CultureInfo.CurrentUICulture.Name);
        Console.WriteLine("CLR: " + Environment.Version);
        Console.WriteLine("WebHeaderCollection ships in: "
                          + typeof(WebHeaderCollection).Assembly.GetName().Name);
        Console.WriteLine();

        Headers();
        Console.WriteLine();
        MediaTypes();
        Console.WriteLine();
        Paths();
    }

    /// <summary>Whether `WebHeaderCollection` can hold a response, and read a request.</summary>
    private static void Headers()
    {
        Console.WriteLine("== WebHeaderCollection ==");

        var writing = new WebHeaderCollection();
        foreach (var name in new[]
                 {
                     "Allow", "Location", "Content-Length", "Transfer-Encoding", "Connection", "Bad Name"
                 })
        {
            Say(name, () => { writing.Add(name, "v"); return "accepted"; });
        }

        Say("value carrying CRLF", () =>
        {
            writing.Add("X-T", "a\r\nInjected: yes");
            return "accepted";
        });

        // The other direction: could it read a request's header block instead of the reader here?
        var reading = new WebHeaderCollection();
        Say("Add(\"A: b\")", () => { reading.Add("A: b"); return reading["A"]; });
        Say("Add(\"A: c\") again", () => { reading.Add("A: c"); return reading["A"]; });
        Say("Add(\" folded\")", () => { reading.Add(" folded"); return "accepted"; });
        Say("Add(\"no-colon\")", () => { reading.Add("no-colon"); return "accepted"; });
    }

    /// <summary>Whether `ContentType` can decide what a body claims to be.</summary>
    private static void MediaTypes()
    {
        Console.WriteLine("== System.Net.Mime.ContentType ==");

        foreach (var value in new[]
                 {
                     "application/json",
                     "application/json; charset=utf-8",
                     "APPLICATION/JSON",
                     "text/plain",
                     "nonsense",
                     "application/json;;;",
                     string.Empty
                 })
        {
            Say(Show(value), () => new ContentType(value).MediaType);
        }
    }

    /// <summary>What `Uri` does to a path this transport has to keep as it arrived.</summary>
    private static void Paths()
    {
        Console.WriteLine("== System.Uri ==");

        foreach (var raw in new[] { "/entries/a%2Fb", "/entries/../x", "/entries/%2E%2E" })
        {
            Say(raw + " AbsolutePath", () => new Uri("https://h" + raw).AbsolutePath);
            Say(raw + " LocalPath", () => new Uri("https://h" + raw).LocalPath);
        }
    }

    private static string Show(string value)
    {
        return value.Length == 0 ? "(empty)" : value;
    }

    private static void Say(string what, Func<string> answer)
    {
        string said;
        try
        {
            said = answer();
        }
        catch (Exception error)
        {
            said = "REFUSED " + error.GetType().Name;
        }

        Console.WriteLine("  " + what.PadRight(38) + " -> " + said);
    }
}
