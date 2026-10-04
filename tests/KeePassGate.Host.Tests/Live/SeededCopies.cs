using System;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Two copies of the documentation's database, each of which replaced the authority they
    /// shared, written down with the clients each then admitted: what the picture of the authority
    /// chooser is taken of.
    /// </summary>
    /// <remarks>
    /// **Written down for the reason `SeededAuthority` is**, and more of it. The chooser shows two
    /// fingerprints, when each authority was created and until when it and its host certificate
    /// are valid, and for the selected one every client's name, profiles and admission time. The
    /// plugin's ECDSA picks a random k, so minted, every one of those would differ on every run.
    ///
    /// **Two new authorities rather than `SeededAuthority` beside one**, because that is the state a
    /// merge of two recreates leaves: each copy retired the authority both started from, and each
    /// holds one of its own that the other has never seen.
    ///
    /// **The dates tell one story.** The copy on this machine recreated first and paired two
    /// clients, one of which the audit here saw last on the date below; the other copy recreated a
    /// week later and paired one client of its own. Minted by the plugin's own `AuthorityIssuer`
    /// with its clock fixed to those moments, which is why each certificate starts five minutes
    /// before the moment it was issued, as every certificate the plugin issues does.
    ///
    /// **Throwaway key material over a database of invented content**, like the rest of this
    /// fixture: it guards nothing and signs for nobody.
    /// </remarks>
    internal static class SeededCopies
    {
        /// <summary>The authority the copy on this machine made when it recreated.</summary>
        public const string ThisCopyAuthority =
            "MIID3wIBAzCCA5sGCSqGSIb3DQEHAaCCA4wEggOIMIIDhDCCAW0GCSqGSIb3DQEHAaCCAV4EggFaMIIBVjCCAVIGCyqGSIb3"
            + "DQEMCgECoIHMMIHJMBwGCiqGSIb3DQEMAQMwDgQIH2OAMj3ThHkCAgfQBIGo53u18IPfrPUIe966WC34t8fNiaVTGjb/mduy"
            + "xHlUIRbcAM7rqMpgIWBRViVZbs4dugar6GqNLRyXGHP2Qpwa6NsEVc/P/4CYrR8hAL033yEgXBSUaYURGAypBcHLS90W2Gt2"
            + "AwSacGN0EFV9dm+siMVxBxfwja+pNMkeU0tZPaPIpiJALxVdfitqnrd+Aa8Yqh+6oeSBXZKX43OoCSe5w0Kmky10zYkcMXQw"
            + "EwYJKoZIhvcNAQkVMQYEBAEAAAAwXQYJKwYBBAGCNxEBMVAeTgBNAGkAYwByAG8AcwBvAGYAdAAgAFMAbwBmAHQAdwBhAHIA"
            + "ZQAgAEsAZQB5ACAAUwB0AG8AcgBhAGcAZQAgAFAAcgBvAHYAaQBkAGUAcjCCAg8GCSqGSIb3DQEHBqCCAgAwggH8AgEAMIIB"
            + "9QYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQMwDgQIbK18FD2CwuYCAgfQgIIByGAin/Txq/0eBFdGVtwVS7s3MJHe2St+NdTP"
            + "jujPsFr7dJci6RC+L1oxdFl8Vsc7I73X+0WuWNKpkr3TFyXVFMDsyHUDXMJig3GxgrEZAJDVT39/18zDMtyz5MvbBBZKvPyV"
            + "nJ+OdAKJzyaAh6e2STJVNUkZEILy38ge+iwg9O5Yox+NdXB86oLKQYJRJIs7lscaMLFvLST/DD/OfM75rhQfdS5ZxXV3a79l"
            + "+WXdxvV1RbiHjOP4g4RF8mH5548Mg7uKx4HDyp6+WLjQuY7u8yizjcu5gjDfIR8fd/upnQQ0vLcy/dqKU9sqYN2LfTmMDDqm"
            + "mEoC5gEX0U3jYb/RYw0EzyrrRcqLfxY7JgHUlD8a/0cGIVPSXbf2cjS4akyIct7vS9i3UcCj+L1zHcunb/UdI6QoNmuo5iSg"
            + "qDgB0Je5F+z14gX22Aj2F6ViH3+Kc8aF7McgSBukl5vX7AA6074hUQ9pHo5eIt/uojvk0lgU1zhteR2eK8lAExGM4R+yuJHz"
            + "P1yL75t3CCm7Xw3WXlxXVfM9Y5kPD2lwQiNwbZsk0xjiCG0MSGkd7X8KW71JgNaHiTTQOld1Qi9izwY7DxiveYLjPgZy/DA7"
            + "MB8wBwYFKw4DAhoEFKKSjJVHqoCeQluMTluzseFM3SvDBBQIFFCBbROYimYN5nZ1ffOutiF+TgICB9A=";

        /// <summary>The host certificate that authority vouches for.</summary>
        public const string ThisCopyHost =
            "MIIEFwIBAzCCA9MGCSqGSIb3DQEHAaCCA8QEggPAMIIDvDCCAW0GCSqGSIb3DQEHAaCCAV4EggFaMIIBVjCCAVIGCyqGSIb3"
            + "DQEMCgECoIHMMIHJMBwGCiqGSIb3DQEMAQMwDgQIdZ1HvbdAy4QCAgfQBIGoNWPoLaUiZIBqumhatjMcK2A56STyyjMUduWe"
            + "vpQvor3uy5jvSD3thGZBKxl9eMe+/JwhWXozYOgZIenPkBSBguiXuvKuXeU87LDM+ENYVNJFHVz4Kh8o6SJW2zZu2E3k+oml"
            + "ED681YtaeihYPqUmJ8xH4ISH+iIHjTIHqliU8LyayEXPekZU8s1oLGCYci/E48Bsutxq1FbtwFrcbMwKEAgeLplFCmB5MXQw"
            + "EwYJKoZIhvcNAQkVMQYEBAEAAAAwXQYJKwYBBAGCNxEBMVAeTgBNAGkAYwByAG8AcwBvAGYAdAAgAFMAbwBmAHQAdwBhAHIA"
            + "ZQAgAEsAZQB5ACAAUwB0AG8AcgBhAGcAZQAgAFAAcgBvAHYAaQBkAGUAcjCCAkcGCSqGSIb3DQEHBqCCAjgwggI0AgEAMIIC"
            + "LQYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQMwDgQIqTMg9RHDKJkCAgfQgIICAAn2rUvHnxtopKySB0lZam9m5QW5HOSHLFYa"
            + "4AhFaqCXahuxKaOedDs+RlZI62/pDFOuq0qUAl/vfF2CjbyRzkz2SZ9+P3QPGyfWIJIxNE7BWgGBdScYxtYb50QsMbsZ19ar"
            + "rTn2okxCrhyGm0eT/ed1zjJF4sikoUe4is/vqcoG2tJEw4Za/RVMPRlyNQruA1010mYjXjfoQ6uHnfdrpEij8fAM/gYa1NaY"
            + "UuUw8schnaVAFFeDhC1gk9/JGtIhiLSU0S/nKnNsKHJwJDYNt1niO/2+odMxXXqpPAGNwsRU8qaFFmJgvV2TMlygjg8LZLDa"
            + "b9S8/6vEM3gwlTSB90F0Kk5W+D8e3dGD1xfEo26+z+l0jc15ohePH64K6qJmnmQNhQfsRe1tU61MPaRKnTP/WWY2shxhxlaO"
            + "ziNR7Qx1WxAXDRqfsjjgbWKy8mFtBdTxwSsSHvSLx3btFB03vtqiXX/WeqRR9fR0QWbqEDrNxIoWQ8MyQMXGQLhJd02ZXUtC"
            + "7gcBMZs55dktArxokOuEPGVG9Sk2Wban1q4Wu7+0QUhR5EGKWlDAwafi/v6dKRMidSoJwK9qnkVttaLKdTP2Mkrjpd3502Fq"
            + "CeKU7yElDYIYfZAGexYAHnYDXRxXDVEnXcRTuMILbkhXleXIkAZILsHJkZl1G1xn80WEB1keMDswHzAHBgUrDgMCGgQU0EOr"
            + "iVWBOpSxglFiPjqvEj65iQsEFNE62CGaTTtnvQPnspdyO5CfRGuMAgIH0A==";

        /// <summary>That authority's gate key.</summary>
        public const string ThisCopySniKey = "diJZIQ7dsm4WpBeSe0TEiv8oM/X7yXRACf3GfSEsPaQ=";

        /// <summary>The admission key of the client this copy paired first.</summary>
        public const string AgentKey =
            "KPG.Client.8129125ce624ec1fa79e0f3c03cbd913ba67496a8b2e266752307f3496339623";

        /// <summary>
        /// That client's record: the program the documentation pairs, paired again after the
        /// recreate took its first admission away.
        /// </summary>
        public static readonly string AgentRecord = string.Join(
            "\n",
            new[]
            {
                "v=1",
                "subject=CN=agent-invented",
                "client=8129125ce624ec1fa79e0f3c03cbd913ba67496a8b2e266752307f3496339623",
                "admitted=2026-09-07T08:31:12Z",
                "profiles=reader,ops",
                "certificate=MIIB4zCCAYigAwIBAgIQYkVUMAfIXHgW4veetbs7LzAKBggqhkjOPQQDAjAgMR4wHAYDVQQDExVLZWVQYXNzR2F0ZSBhdXRob3JpdHkwHhcNMjYwOTA3MDgyNjEyWhcNMjcwOTA3MDgzMTEyWjAZMRcwFQYDVQQDEw5hZ2VudC1pbnZlbnRlZDBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABP13Pm9hTffbtRswUP3dXnaGN8d9IFe6NdVoXTjdY2GRIJg++9xTD3RBiq2uLhNc80lt0EpMyLF6IxwM6hJQ1XWjgaowgacwDAYDVR0TAQH/BAIwADAOBgNVHQ8BAf8EBAMCB4AwEwYDVR0lBAwwCgYIKwYBBQUHAwIwHQYDVR0OBBYEFJncuXVTLy/KYfSZMYJjsgl3qC4eMB8GA1UdIwQYMBaAFGDPKwI1j7yNZ1ILumZZTspQhKx9MDIGA1UdEQQrMCmGFGtwZzovL3Byb2ZpbGUvcmVhZGVyhhFrcGc6Ly9wcm9maWxlL29wczAKBggqhkjOPQQDAgNJADBGAiEAyVJR+EXUqmomlJSQ92Pw6S/257X8W6Wgvr8y2XVtIb4CIQCLNrIvtmY+NNMLTVS38bKWJkL+C/jflh8XpIl6BFhFTg=="
            });

        /// <summary>
        /// When the audit on this machine last wrote that client down, which is what the chooser
        /// shows as its last request.
        /// </summary>
        public static readonly DateTimeOffset AgentLastRequest =
            new DateTimeOffset(2026, 9, 25, 16, 42, 10, TimeSpan.Zero);

        /// <summary>The admission key of the second client this copy paired.</summary>
        public const string BackupKey =
            "KPG.Client.f760ca956c2988630ea129f5492eb69fb6a4540c0b9776aa2a31c0b5dd73eeb2";

        /// <summary>
        /// That client's record. Nothing in the audit names it, so the chooser says so, which is the
        /// other answer that column gives.
        /// </summary>
        public static readonly string BackupRecord = string.Join(
            "\n",
            new[]
            {
                "v=1",
                "subject=CN=backup-invented",
                "client=f760ca956c2988630ea129f5492eb69fb6a4540c0b9776aa2a31c0b5dd73eeb2",
                "admitted=2026-09-10T11:05:36Z",
                "profiles=reader",
                "certificate=MIIB0DCCAXagAwIBAgIQYv4ath7uWTieRljZnECO/zAKBggqhkjOPQQDAjAgMR4wHAYDVQQDExVLZWVQYXNzR2F0ZSBhdXRob3JpdHkwHhcNMjYwOTEwMTEwMDM2WhcNMjcwOTEwMTEwNTM2WjAaMRgwFgYDVQQDEw9iYWNrdXAtaW52ZW50ZWQwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAATrtq3muDYz5JYvDtEV5dBFE62wOmwjZcsomV/+SXabCaCbHFmhQCuEKDi94fKQZaG0TM1xudbrTszF7GmsGNKro4GXMIGUMAwGA1UdEwEB/wQCMAAwDgYDVR0PAQH/BAQDAgeAMBMGA1UdJQQMMAoGCCsGAQUFBwMCMB0GA1UdDgQWBBQTCZO32GvSa3qG7N5Z57rWpn8P7zAfBgNVHSMEGDAWgBRgzysCNY+8jWdSC7pmWU7KUISsfTAfBgNVHREEGDAWhhRrcGc6Ly9wcm9maWxlL3JlYWRlcjAKBggqhkjOPQQDAgNIADBFAiA1Toiw8caZRXTkXvtoR8vsjRWeph/EIsWhN4IrsyyH8AIhAPl0IokazMrzt07CTq6GopPLpum5ZusCr3cWbuvQTwe2"
            });

        /// <summary>The authority the other copy made when it recreated, a week later.</summary>
        public const string OtherCopyAuthority =
            "MIID3wIBAzCCA5sGCSqGSIb3DQEHAaCCA4wEggOIMIIDhDCCAW0GCSqGSIb3DQEHAaCCAV4EggFaMIIBVjCCAVIGCyqGSIb3"
            + "DQEMCgECoIHMMIHJMBwGCiqGSIb3DQEMAQMwDgQIAbhVTMjUE2ACAgfQBIGoCi4D9JNfshSPxza2XqV1LhpYuCdwcJp8tA2j"
            + "qmdREFJdceJU1CS6qMw4ZBMLtCNUajSAyBiJukPhKj4N32xtRGsvYLx9/K/LOhFdAeOQWr0NN0XgdV453R9SnlZS73YmXuLq"
            + "5aUVkgzEqQHZ5y6wobJiR1PKkOg1/xBf51OtFu9lbYAuXPsUn2gt5HOw0+uU4N4oE2xaoI52c/rgazC34xRBj2isgHCKMXQw"
            + "EwYJKoZIhvcNAQkVMQYEBAEAAAAwXQYJKwYBBAGCNxEBMVAeTgBNAGkAYwByAG8AcwBvAGYAdAAgAFMAbwBmAHQAdwBhAHIA"
            + "ZQAgAEsAZQB5ACAAUwB0AG8AcgBhAGcAZQAgAFAAcgBvAHYAaQBkAGUAcjCCAg8GCSqGSIb3DQEHBqCCAgAwggH8AgEAMIIB"
            + "9QYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQMwDgQIEbfGb7Xt36sCAgfQgIIByHNQWQMEp8SsxMttcvI2PFpr1uw6DcVcrr63"
            + "3Tb7mb+dXnoCcEjWHpJQf2zkBxWzQibCj9gc3mugJcvsA21opPOkluYxwcy+OBmx+3w8hFOf6IKdXVtMJpJHjMJSz1bTxBbJ"
            + "aW+nHCp4oCVQNkXtZB/90I1MloO0CyvCc9niq1kxFQ3DwCaPZ4KKCfnqyAbvqC+/N8WSa0v0UlxafkLdRkE+WnEZ56Wwj3TY"
            + "lAVnEehMGBmI5fcUma9YTwGH7CB0ouTzLS0CVNVU1W80TV5DwUXHv6nitpsa2RgLJEcXSAoqG/pvlyQvb4OzLCXewnx/Cgji"
            + "p21uuZHeI3vOtsvgGNhQSxv72vpYUnlsKzcZWJLu4vh5fpd2eBDQskCKEITIzgb7oofSc6g4GvuCaKFZqDgMXSeNdt1o/zbS"
            + "/L9ACpUXK1DqbpfbQ6b/zBZdYxIfthZjv+oQFXwDEADN9Osq57endTbbsJOiAQ42nbayBEkTBPh4Gb4DKvxnH96NZ5KTlbYo"
            + "TiCBgWvhpq1TzD5zKumCS5yQrfkJCREU3Tj7qRYy2CMjI1d+JhW8yxDlSe+xTWlccxxkzCxf2W64lLR+bmdgQS6Kb0XWOjA7"
            + "MB8wBwYFKw4DAhoEFNlj6Ha8bXonMSePY8R6ou2r+4CEBBQe82rlZVnHqXBbzGc7B+R2aykTjAICB9A=";

        /// <summary>The host certificate that authority vouches for.</summary>
        public const string OtherCopyHost =
            "MIIEFwIBAzCCA9MGCSqGSIb3DQEHAaCCA8QEggPAMIIDvDCCAW0GCSqGSIb3DQEHAaCCAV4EggFaMIIBVjCCAVIGCyqGSIb3"
            + "DQEMCgECoIHMMIHJMBwGCiqGSIb3DQEMAQMwDgQIe6hxCrqo4O4CAgfQBIGoQa1kAcFXgsvNc3FBCGxIT+Nesed5Pm2cfBZI"
            + "CCbtrM42pkm1oqTsB84GFEyKS6yJaTgdYlqIPyrwdXtDIaQgkQvWeQT0WOCI+vCMUr5nPmqzHyBevREmtBzFZWzXjOJlsf5o"
            + "MPL57aQcCItD0SH5pHqH/fMZddzSzhbkKJ2GlsESIxf12QEcJPwwoL3c9c7g7gC08I8XNOmSwuvmEPDKCS3PmxZqHiepMXQw"
            + "EwYJKoZIhvcNAQkVMQYEBAEAAAAwXQYJKwYBBAGCNxEBMVAeTgBNAGkAYwByAG8AcwBvAGYAdAAgAFMAbwBmAHQAdwBhAHIA"
            + "ZQAgAEsAZQB5ACAAUwB0AG8AcgBhAGcAZQAgAFAAcgBvAHYAaQBkAGUAcjCCAkcGCSqGSIb3DQEHBqCCAjgwggI0AgEAMIIC"
            + "LQYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQMwDgQI+x9ExF9Eit4CAgfQgIICALnxAcJgjwurnvRZFR2hqMpWt/BXV5AAVsnk"
            + "bIkuS7iUoJaZqUst6teCf3Qe+pggAaWBkHMom1XxX0niGf9f1nPu2hCedMzguy49geDhPFhtevMTGsiH8Oq3yqqrdYXeL5Vn"
            + "JzW2uDAyFdsUXJgHFqiw3mY+UvetQTV3iE2JtBgJfaCzcfYa3Vu2CS2NXnqRoMYxRCdHYQ3hGEDD8IqDAs55wD4OBmOLsLp5"
            + "1oyK5iSvMrs25q7GR5aNBdnMIt+4F7Z1bA/2EDV3wdTYCkGGMJJZVfjO35BpIZ+8fwE0DS9uBDQl9WTzuKDQPyqJh0fHDHSg"
            + "DVqOBAdwCeZdHV3VmJV3+nRYvfBkrx1cJqpULUKpr1gf2Y04uc3rXQb5GzKe6nM0Qz4dcU9Isu05SBbnbwY0zfz4OdahJWjO"
            + "tERM0HCP4AKpvenDYIsE2qHt77TfzN37M5CigYkbB3FksrhJS5u3ujWONDDCxQMuUBRrpJrBVlrGgYEaOjbOjb8A2+Z+sxib"
            + "oM0MwevWI7z83cR+6rEhAQu/4+8jWc2rkZeuu6KBh1esc+IR0l6SU6GaFHtMEvW2ByQ2PoMNjAO0UyAYNgiauB3olr2jgYKC"
            + "lvZTwj+Nr34PahaV7lEGtv1H4D95gUtbM07/HSxwFb2XhdApGjpXyUReST7vhpp96lRR09MEMDswHzAHBgUrDgMCGgQUyWE7"
            + "lbPS2BTBhQD5+l5x7ezRYLwEFCyQZOWLB2Ikqs9NlTecr6tsi1p4AgIH0A==";

        /// <summary>That authority's gate key.</summary>
        public const string OtherCopySniKey = "7xPEr4HWfjm4EXGdllfsDnlr3KtMRkEqUYYMKFzRAKs=";

        /// <summary>The admission key of the one client the other copy paired.</summary>
        public const string OtherAgentKey =
            "KPG.Client.37435b34c9abe506c8ccca9259027d1b55f98552fc6937cf1d55c85393bbb07e";

        /// <summary>That client's record.</summary>
        public static readonly string OtherAgentRecord = string.Join(
            "\n",
            new[]
            {
                "v=1",
                "subject=CN=agent-fictitious",
                "client=37435b34c9abe506c8ccca9259027d1b55f98552fc6937cf1d55c85393bbb07e",
                "admitted=2026-09-14T17:02:08Z",
                "profiles=ops",
                "certificate=MIIBzzCCAXSgAwIBAgIQBdNIxwhHXLghEsILQcNRNzAKBggqhkjOPQQDAjAgMR4wHAYDVQQDExVLZWVQYXNzR2F0ZSBhdXRob3JpdHkwHhcNMjYwOTE0MTY1NzA4WhcNMjcwOTE0MTcwMjA4WjAbMRkwFwYDVQQDExBhZ2VudC1maWN0aXRpb3VzMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAELwdOl4+mP1Z06Q9atuXx48xxyLWpOeyUwqc9ZWBEOu/6U1V80YPoRtQa8zT0GvM4ildwSMOPVeQMLxcnqvUJhKOBlDCBkTAMBgNVHRMBAf8EAjAAMA4GA1UdDwEB/wQEAwIHgDATBgNVHSUEDDAKBggrBgEFBQcDAjAdBgNVHQ4EFgQUHUwW77deNuxe3GBlcI2kZzKtVeMwHwYDVR0jBBgwFoAUcFO5qmMisYfL28p69+GMPxlaCvgwHAYDVR0RBBUwE4YRa3BnOi8vcHJvZmlsZS9vcHMwCgYIKoZIzj0EAwIDSQAwRgIhAIYaljJh/uOo2quoPGG0T/Dl7uNUQtkxXQigolWUPatEAiEAk2+4YsK4cZzNMzgH0CdezU0lhVixqdlrX73SSTJDOI8="
            });
    }
}
