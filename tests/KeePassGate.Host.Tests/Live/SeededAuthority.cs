namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// One authority and one admitted client, written down rather than minted, so that a picture of
    /// them is the same picture every time.
    /// </summary>
    /// <remarks>
    /// **Why written down.** The plugin signs with .NET's `CertificateRequest`, whose ECDSA picks a
    /// random k, so minting the same certificate twice gives different bytes and a different
    /// fingerprint. A fingerprint that does not move therefore means bytes that do not move, and
    /// bytes that do not move have to be kept somewhere. These are them.
    ///
    /// **No `.kdbx` is committed.** What is here is what the plugin keeps under two `KPG` keys in
    /// the database's own custom data: an authority's three parts, which it writes as one
    /// document, and one client's admission record. A database that holds them is crafted when a
    /// test runs, and nothing binary goes into the repository. The gate
    /// tests here mint a fresh authority per run, because what they assert is that a request is
    /// answered rather than what a fingerprint reads; the fixture that wants the same bytes twice
    /// is the admission record's, in step 7.
    ///
    /// **This is a throwaway authority over a database of invented content.** Its private key is
    /// here in the open on purpose, the way `ThrowawayKeePass.DatabasePassword` is: it guards
    /// nothing, it signs for nobody, and a reader who finds it should be able to see that at once.
    /// </remarks>
    internal static class SeededAuthority
    {
        /// <summary>The authority's own certificate and key.</summary>
        public const string Authority =
            "MIID5wIBAzCCA6MGCSqGSIb3DQEHAaCCA5QEggOQMIIDjDCCAW0GCSqGSIb3DQEHAaCCAV4EggFaMIIBVjCCAVIGCyqGSIb3"
            + "DQEMCgECoIHMMIHJMBwGCiqGSIb3DQEMAQMwDgQI570gqGNT0FMCAgfQBIGo8IR7OST1/U00zrPFuXIY6VQWTohBL5J+HUl4"
            + "MAfzZuHeBo5RonKmd1Efmzi6QkFZgqWO5342krnvjeCwWdhPIOW7xT9iNNhxFDw/D/U/JdP7CwXrQPf07iin7XT9nUELqLxV"
            + "CGlX3gMnz44Hg0JmZ2+LZPvoe2hiTZu+No2giNUbM949WH+DcK4C/wzjFLhM04ixKxBur+j3J7cfIk8ugXDuVXD55sySMXQw"
            + "EwYJKoZIhvcNAQkVMQYEBAEAAAAwXQYJKwYBBAGCNxEBMVAeTgBNAGkAYwByAG8AcwBvAGYAdAAgAFMAbwBmAHQAdwBhAHIA"
            + "ZQAgAEsAZQB5ACAAUwB0AG8AcgBhAGcAZQAgAFAAcgBvAHYAaQBkAGUAcjCCAhcGCSqGSIb3DQEHBqCCAggwggIEAgEAMIIB"
            + "/QYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQMwDgQIMjT03OyMpFMCAgfQgIIB0G4rbd9vQO0GEUYKwglehB5+sesxnVhiopvu"
            + "2DKrLZE3Bmy+3sg9VcG03sfL2Z7v8jGYIfJ9NDk5AJnaN9S+ePNjywG8uEd/oDAjOmRCzYiPPN7Mifsn5RPIoo5wC+buJwaO"
            + "buDo+ASaG9QCOLpP/ISR4G/dsaxHif6/lRJw5j5G/H+fbto++8GEvShsDa8kWJ+VzRPzNbsInQlcoyv1oq9HrIoDE/jr6CFw"
            + "77fMcdq/r3eIBsQSWp4cioKxReDF68qhRs2AvLhVkkzfIDvjVneP27Sn05ZvzXuHnMSUryr830GHYxfjMsJwRt+YltVQgjVc"
            + "UDfOeqAFJaEra9pjEQHUA2sxBIJrBTmGXIIaHFaFdIZmOFQVrWQeOFlppzVB8wC1T7Z0VTOF7QqvEQMwIN5vHUoyJlHH9rbu"
            + "/LNelyMjt1LoSouBiqjOvXH5okvXC9qy/tBgLkm7Wu+/pCQH2sXTs7+sXRJkQInm4YUvOoOLWxZqfk5GgN+xORVj3tRckJpU"
            + "TNcspF7IBnTyJuxPzHnr7td6Ao1fNedmScIkoYuv6J7EpHVr68pl1Lr3rOzb2tCSX4KNf2rTUcqqHMdxl6eNYfU35zQhZ2hw"
            + "viOYv3TpMDswHzAHBgUrDgMCGgQUr871mSxCPR3iyw2gAuNq71+BRRkEFJJrIm5YpBHG7yG+xE3TqtghMGJRAgIH0A==";

        /// <summary>The host certificate the gate presents.</summary>
        public const string Host =
            "MIIEFwIBAzCCA9MGCSqGSIb3DQEHAaCCA8QEggPAMIIDvDCCAW0GCSqGSIb3DQEHAaCCAV4EggFaMIIBVjCCAVIGCyqGSIb3"
            + "DQEMCgECoIHMMIHJMBwGCiqGSIb3DQEMAQMwDgQIHNTji8DMK9QCAgfQBIGo2/QGVEYonqOh6LZWHGp0TTTI++Hl7JncBZc/"
            + "vXY0TcfWvpVBu9Ch1kN+uwUlW5d1tykFxKNUWaokGXfFhT+tbpJQjKpKBsw020P0VykME/wbc/uOZd0PI+UwGlaLWT/iSOJz"
            + "RVTUyTlBzdEAj56J5drVcjVymG1G90oDDOBEeiG7918wCU3Y3iUmSemD+tJD9ZC8hITbtXfLbrvRbfvbVrxikmttMS1qMXQw"
            + "EwYJKoZIhvcNAQkVMQYEBAEAAAAwXQYJKwYBBAGCNxEBMVAeTgBNAGkAYwByAG8AcwBvAGYAdAAgAFMAbwBmAHQAdwBhAHIA"
            + "ZQAgAEsAZQB5ACAAUwB0AG8AcgBhAGcAZQAgAFAAcgBvAHYAaQBkAGUAcjCCAkcGCSqGSIb3DQEHBqCCAjgwggI0AgEAMIIC"
            + "LQYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQMwDgQIPq51v3OKyZUCAgfQgIICANd9aKNVkBjK/rqLlswTplgO5Ds7G4NVNLZz"
            + "ElIkxj09CZB6HvjApLOFDQu2CvGys3UO+/aW+T1tyxaZJWxmhd4ehIziYGUz4YZWZT2WYPW4cwLMjP8B0HzpBKRTY4W+c1kU"
            + "6RBkcO7ma+qIas5MQiHTDOdxkrLtRW6dBohMOxtaQcDsLygkgYHLMePs9JwYBCepgJKFEDE6abTSS60qe9r2rplmfh6sessX"
            + "TRDGqAtrg1j6uVT9nFz8FEcaWhptwj0a13vBIUjm0zf+Pvb2abBgSH+lrrILLqZyn24/FV3eoQLsz4gl+uEnhCV+4stnvi58"
            + "yMjdBe7A0Uqfx8263718GEqnW1zIVrPSXpkszVeDAUjGLYu2HBpZ5c3dTkBxqYpc9qhPGcsWAbt76o/0QMkgj4SaPe4qG++Q"
            + "fmWsyEdtR1Bn1rKD4p/YfW7AI53qMjSWsdBkY+SU+cHPAcSse0iE5O2SShMT4D1rWOG5BXPB68S5LREIyVe3amss4U1MWtB3"
            + "GcJ6lMKdvVoX6hQqcgUwM7SkBiljtAN4ozsFc5ygDiJ8xCdtSCMDtr6C7RuMsTPMVXqStjmC6a1jwENjlmQc5eJCtZ72+QyU"
            + "hOq2tFkfLCimgUfosPwkcnwq7/kPj4VJd+r9nPKP+3UmS4FiChba2dQPRsGNBOm3fGLvoGl+MDswHzAHBgUrDgMCGgQUxRff"
            + "gDg8zSJxikKKwPUXfTuo6FMEFEplV/Mra3Gqnb+Zh22rA8b2QIIDAgIH0A==";

        /// <summary>The key the rotating name is computed from.</summary>
        public const string SniKey = "1wPpU0rW182vOL1F0XbHxyKHQ59z5iakQs0UmjLfoqA=";

        /// <summary>The admission key that one client is recorded under.</summary>
        public const string ClientKey =
            "KPG.Client.c9d74273af508a25252797f2ceb18288a38bd3ea7aab43bc500629ddb5b5452c";

        /// <summary>
        /// That client's record, admitted at a date that does not move.
        /// </summary>
        /// <remarks>
        /// Joined from its lines rather than written as one string with escapes in it, because the
        /// separator matters and an escape is the thing most likely to be got wrong by whatever
        /// writes this file next.
        /// </remarks>
        public static readonly string ClientRecord = string.Join(
            "\n",
            new[]
            {
                "v=1",
                "subject=CN=agent-invented",
                "client=c9d74273af508a25252797f2ceb18288a38bd3ea7aab43bc500629ddb5b5452c",
                "admitted=2026-08-22T08:38:01Z",
                "profiles=reader,ops",
                "certificate=MIIBrTCCAVKgAwIBAgIQfUczW38muXkJ2k4smPkY+jAKBggqhkjOPQQDAjAgMR4wHAYDVQQDExVLZWVQYXNzR2F0ZSBhdXRob3JpdHkwHhcNMjYwODIyMDgzMzAxWhcNMjcwODIyMDgzODAxWjAZMRcwFQYDVQQDEw5hZ2VudC1pbnZlbnRlZDBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABAS78Qqi28/5+7BQmpuOt0QCKeOcPrmdVHdRmXmpguis3LmxeQIknOvn2iFyynH/v9hZF2fYOXc/u0Qs+eys7/qjdTBzMAwGA1UdEwEB/wQCMAAwDgYDVR0PAQH/BAQDAgeAMBMGA1UdJQQMMAoGCCsGAQUFBwMCMB0GA1UdDgQWBBTDHz21u+WoPt0vbC9Z07n/tUe06zAfBgNVHSMEGDAWgBT495o5wolADbrniyVCndXKANXsazAKBggqhkjOPQQDAgNJADBGAiEA/iYw9xJmUgLvzmPZsj1QfFzFqAFthDoVK30EpcYjQ8sCIQCb2ww2hGHccyKgogLTv7X5K6hVtBF1+jcX88CPV92G1A=="
            });
    }
}
