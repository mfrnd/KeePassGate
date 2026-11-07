using System;
using System.IO;
using System.Text;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>What actually goes on the wire, which is the only place a HEAD can be checked.</summary>
    /// <remarks>
    /// The router decides what a HEAD answers; this decides what a HEAD *sends*. They were checked
    /// only at the first of those, which is how an answer went out with a length that disagreed
    /// with the content it did not have.
    /// </remarks>
    [TestFixture]
    public sealed class HttpResponseTests
    {
        [Test]
        public void An_ordinary_answer_carries_its_content_and_says_how_long_it_is()
        {
            var written = Write(200, "{\"ok\":true}", false);

            Assert.That(written, Does.Contain("Content-Length: 11"));
            Assert.That(written, Does.EndWith("{\"ok\":true}"));
        }

        [Test]
        public void A_head_answer_carries_no_content_and_claims_no_length()
        {
            // RFC 9112 section 6.3: a response to HEAD is terminated by the blank line and cannot
            // carry content. RFC 9110 section 8.6: a `Content-Length` that is not what a GET would
            // have sent must not be sent at all, which `Content-Length: 0` here would be. Section
            // 9.3.2 permits omitting a field determined only while generating the content.
            var written = Write(200, "{\"ok\":true}", true);

            Assert.That(written, Does.Not.Contain("Content-Length"));
            Assert.That(written, Does.EndWith("\r\n\r\n"));
        }

        [Test]
        public void A_head_refusal_carries_no_content_either()
        {
            // The 200 was never the only answer a HEAD gets. Every refusal is a response, and the
            // rule is about responses.
            var written = Write(401, HttpResponse.ErrorBody("unpaired"), true);

            Assert.That(written, Does.StartWith("HTTP/1.1 401 "));
            Assert.That(written, Does.Not.Contain("unpaired"));
            Assert.That(written, Does.Not.Contain("Content-Length"));
        }

        [Test]
        public void Every_refusal_that_is_worth_retrying_says_when_to_try_again()
        {
            // RFC 9110 section 10.2.3. Decided here rather than at each of the four places that
            // answer `503`, so that a client sees one behavior and a refusal added later cannot
            // be the one that forgets it.
            Assert.That(Write(503, HttpResponse.ErrorBody("busy"), false), Does.Contain("Retry-After: 2\r\n"));
            Assert.That(Write(503, HttpResponse.ErrorBody("locked"), false), Does.Contain("Retry-After: 2\r\n"));
        }

        [Test]
        public void A_head_asking_about_a_refusal_is_told_the_same_thing()
        {
            // A HEAD answers with the fields its GET would, which is the whole of what a HEAD is
            // for: only the content goes. So the fields telling a client what to do next stay.
            var written = Write(503, HttpResponse.ErrorBody("locked"), true);

            Assert.That(written, Does.Contain("Retry-After: 2\r\n"));
            Assert.That(written, Does.Contain("KPG-Unavailable: locked\r\n"));
            Assert.That(written, Does.EndWith("\r\n\r\n"));
        }

        [Test]
        public void Every_refusal_that_is_worth_retrying_names_its_word_in_a_field_as_well()
        {
            // For the answer to a HEAD, which has no body to name it in: a client asking whether
            // something was there told `busy` from `locked` by nothing, and waited out neither. A GET carries it too, since a HEAD carries the fields a GET would.
            foreach (var word in new[] { "busy", "locked", "undecided" })
            {
                foreach (var head in new[] { false, true })
                {
                    Assert.That(Write(503, HttpResponse.ErrorBody(word), head),
                        Does.Contain(HttpResponse.UnavailableField + ": " + word + "\r\n"),
                        word + (head ? " to a HEAD" : " to a GET"));
                }
            }
        }

        [Test]
        public void The_field_names_the_body_s_word_whatever_else_the_body_holds()
        {
            // An approval held is answered with the moment it is held until beside the word.
            var written = Write(503, "{\"error\":\"busy\",\"heldUntil\":\"2026-11-07T10:00:00Z\"}", true);

            Assert.That(written, Does.Contain("KPG-Unavailable: busy\r\n"));
        }

        [Test]
        public void A_body_naming_no_word_names_none_in_the_field_either()
        {
            foreach (var json in new[] { "{}", HttpResponse.ErrorBody("not a word"), "not json", "" })
            {
                Assert.That(Write(503, json, false), Does.Not.Contain("KPG-Unavailable"), json);
            }
        }

        [Test]
        public void Nothing_but_a_refusal_worth_retrying_names_a_word_in_the_field()
        {
            foreach (var status in new[] { 200, 400, 401, 404, 409, 500, 504 })
            {
                Assert.That(Write(status, HttpResponse.ErrorBody("busy"), false),
                    Does.Not.Contain("KPG-Unavailable"), "status " + status);
            }
        }

        [Test]
        public void Nothing_else_is_told_to_come_back_later()
        {
            // `Retry-After` is also allowed on a `3xx` and on `429`, neither of which this gate
            // sends, and on nothing else. A refusal a client cannot fix by waiting must not read
            // as one it can.
            foreach (var status in new[] { 200, 400, 401, 404, 409, 500, 504 })
            {
                Assert.That(Write(status, HttpResponse.ErrorBody("no"), false), Does.Not.Contain("Retry-After"),
                    "status " + status);
            }
        }

        [Test]
        public void A_caller_cannot_send_a_second_opinion_about_when_to_come_back()
        {
            // The same rule as `Content-Length` and the rest: what this writer decides, nothing
            // above it may decide again. Two of them on the wire is two answers to one question.
            var headers = new System.Net.WebHeaderCollection
            {
                { "Retry-After", "600" },
                { "KPG-Unavailable", "teapot" },
            };

            using (var stream = new MemoryStream())
            {
                HttpResponse.Write(stream, 503, HttpResponse.ErrorBody("busy"), false, headers);

                var written = Encoding.UTF8.GetString(stream.ToArray());
                Assert.That(written, Does.Contain("Retry-After: 2\r\n"));
                Assert.That(written, Does.Not.Contain("600"));
                Assert.That(written, Does.Contain("KPG-Unavailable: busy\r\n"));
                Assert.That(written, Does.Not.Contain("teapot"));
            }
        }

        private static string Write(int status, string json, bool head)
        {
            using (var stream = new MemoryStream())
            {
                HttpResponse.Write(stream, status, json, head, null);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
