"""The three pieces of the protocol that are pure functions, against the plugin's own output.

**The vectors are the point of this file.** Everything here could pass while the client and the
plugin agreed on something neither document describes, because both halves would be this code. So
the expected values were produced by the plugin itself, from `PairingToken`, `SniName` and
`PairingProof`, over inputs written down beside them. These hold the client to them, and so fail
here if the client's construction changes; `ProtocolVectorsTests` holds the plugin to the same
values, so a change to the plugin's fails in its own tier. Neither waits for the first real pairing.

The inputs are deliberately boring: 0 to 31 for a token, 100 to 115 for a nonce, 200 to 231 for a
challenge, 1 to 32 for a binding. Nothing about them is secret and nothing about them is realistic,
which is what makes them reproducible on the other side.
"""

from __future__ import annotations

import base64

import pytest

from pykeepassgate import proofs, sni
from pykeepassgate import token as token_module
from pykeepassgate.errors import MalformedToken

TOKEN = bytes(range(32))
NONCE = bytes(range(100, 116))
CHALLENGE = bytes(range(200, 232))
BINDING = bytes(range(1, 33))

# Produced by the plugin. See the module docstring.
TOKEN_TEXT = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"
SNI_NAME = "6465666768696a6b6c6d6e6f70717273.82b166a6ea164ce7bde4cb0bbec5b7e5.kpg"
SNI_TAG = "grFmpuoWTOe95MsLvsW35Q=="
PROOF_CLIENT = "OhAiWUjmxuLkeL50QLEoOk12eI9H9ZDV4/ljctZr1b0="
PROOF_SERVER = "bJ23EO4jD3y4swTo2uwdsO1Equ0o3VKTmQqVT0pamME="
PROOF_ASKED = "YWdlbnQtaW52ZW50ZWQKcmVhZGVyLHdyaXRlcgpQMzBE"
PROOF_ASKED_NO_LIFETIME = "YWdlbnQtaW52ZW50ZWQKcmVhZGVyLHdyaXRlcgo="
PROOF_REQUEST = "BdArJ130P+Sjp+VtBYmmfWXD1+lMQV5xsAiNUz6JabY="
BINDING_OF_TEN = "yEjhAT+fBKnWP6Q85/1K8DUVLHxmmkpAS2cQfO5fLk4="


def unb64(text: str) -> bytes:
    return base64.b64decode(text)


class TestToken:
    def test_a_token_is_written_the_way_the_plugin_writes_it(self) -> None:
        assert token_module.encode(TOKEN) == TOKEN_TEXT

    def test_what_the_plugin_wrote_reads_back_as_what_it_wrote(self) -> None:
        assert token_module.decode(TOKEN_TEXT) == TOKEN

    def test_a_token_is_forty_three_characters_and_carries_no_padding(self) -> None:
        assert len(TOKEN_TEXT) == token_module.TEXT_LENGTH
        assert "=" not in TOKEN_TEXT

    @pytest.mark.parametrize(
        "text",
        [
            "",
            "too-short",
            TOKEN_TEXT + "x",
            TOKEN_TEXT[:-1] + "+",  # base64, but not the URL alphabet
            TOKEN_TEXT[:-1] + "/",
            TOKEN_TEXT[:-1] + "=",  # padding, which the plugin never writes
        ],
    )
    def test_anything_that_is_not_exactly_a_token_is_refused(self, text: str) -> None:
        # Fail closed, the same way and for the same reason the plugin's own decoder does: a token
        # that almost reads is a token that pairs with the wrong thing.
        with pytest.raises(MalformedToken):
            token_module.decode(text)

    def test_a_token_spelled_with_its_spare_bits_set_is_refused(self) -> None:
        # 43 characters carry 258 bits for a 256 bit token, so the last character has two the
        # token does not use: three other spellings decode to the same bytes, and the plugin
        # writes only one of them. Reading the others back would make one token four.
        alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
        spare = TOKEN_TEXT[:-1] + alphabet[alphabet.index(TOKEN_TEXT[-1]) + 1]

        assert base64.urlsafe_b64decode(spare + "==") == TOKEN, "the same bytes, spelled again"

        with pytest.raises(MalformedToken):
            token_module.decode(spare)

    def test_a_token_that_is_not_thirty_two_bytes_cannot_be_written(self) -> None:
        with pytest.raises(MalformedToken):
            token_module.encode(b"short")


class TestSni:
    def test_the_name_is_the_one_the_gate_would_resolve(self) -> None:
        assert sni.name_for(TOKEN, NONCE) == SNI_NAME

    def test_the_tag_is_the_truncated_hmac_the_gate_recomputes(self) -> None:
        assert sni.tag(TOKEN, NONCE) == unb64(SNI_TAG)

    def test_the_tag_is_sixteen_bytes_because_a_dns_label_is_sixty_three_characters(self) -> None:
        assert len(sni.tag(TOKEN, NONCE)) == sni.TAG_LENGTH
        for label in SNI_NAME.split("."):
            assert len(label) <= 63

    def test_a_fresh_name_is_a_different_name(self) -> None:
        first, one = sni.new_name(TOKEN)
        second, other = sni.new_name(TOKEN)

        assert first != second, "a passive observer would see the same name twice"
        assert one != other

    def test_a_fresh_name_is_the_name_for_the_nonce_it_hands_back(self) -> None:
        # The nonce is returned because the proofs are computed over it. If it were not the one the
        # name was built from, every proof after it would be over something the gate never saw.
        name, nonce = sni.new_name(TOKEN)
        assert name == sni.name_for(TOKEN, nonce)

    def test_a_nonce_of_the_wrong_length_is_refused_rather_than_padded(self) -> None:
        with pytest.raises(ValueError, match="nonce"):
            sni.name_for(TOKEN, b"short")


class TestProofs:
    def test_the_binding_is_sha256_over_the_certificate(self) -> None:
        assert proofs.binding(bytes(range(1, 11))) == unb64(BINDING_OF_TEN)

    def test_the_client_proof_is_what_the_gate_expects_to_receive(self) -> None:
        assert proofs.client(TOKEN, BINDING, NONCE) == unb64(PROOF_CLIENT)

    def test_the_server_proof_is_what_the_gate_answers_with(self) -> None:
        assert proofs.server(TOKEN, BINDING, NONCE, CHALLENGE) == unb64(PROOF_SERVER)

    def test_what_a_request_commits_to_is_the_name_the_profiles_and_the_lifetime(self) -> None:
        assert proofs.asked("agent-invented", ["reader", "writer"], "P30D") == unb64(PROOF_ASKED)

    def test_a_request_asking_for_no_lifetime_commits_to_an_empty_line_for_it(self) -> None:
        assert proofs.asked("agent-invented", ["reader", "writer"], None) == unb64(
            PROOF_ASKED_NO_LIFETIME
        )

    def test_the_request_proof_covers_the_whole_exchange(self) -> None:
        asked = proofs.asked("agent-invented", ["reader", "writer"], "P30D")
        assert proofs.request(TOKEN, BINDING, NONCE, CHALLENGE, asked) == unb64(PROOF_REQUEST)

    def test_a_lifetime_added_taken_off_or_changed_is_a_different_request(self) -> None:
        asked = proofs.asked("agent-invented", ["reader"], "P30D")

        assert asked != proofs.asked("agent-invented", ["reader"], None)
        assert asked != proofs.asked("agent-invented", ["reader"], "P31D")

    def test_no_proof_can_be_replayed_as_another(self) -> None:
        # Three labels exist for exactly this: the same inputs under a different label are a
        # different value, so a captured hello proof is not a request proof.
        client = proofs.client(TOKEN, BINDING, NONCE)
        server = proofs.server(TOKEN, BINDING, NONCE, CHALLENGE)
        request = proofs.request(TOKEN, BINDING, NONCE, CHALLENGE, b"")

        assert len({client, server, request}) == 3

    def test_the_parts_cannot_be_shuffled_across_the_boundary_between_them(self) -> None:
        # `_compute` length prefixes every part, which is the whole of why this holds: the same
        # bytes divided differently between two parts do not produce the same proof.
        one = proofs.request(TOKEN, BINDING, NONCE, b"abc", b"de")
        other = proofs.request(TOKEN, BINDING, NONCE, b"ab", b"cde")

        assert one != other

    def test_the_commitment_is_unambiguous_only_inside_the_names_the_gate_allows(self) -> None:
        # `asked` prefixes nothing: the name is separated from the profiles by a newline and the
        # profiles from each other by commas, so outside the charset it is ambiguous. Each of
        # these builds one commitment out of two different requests.
        assert proofs.asked("agent", ["a\nb"], None) == proofs.asked("agent\na", ["b"], None)
        assert proofs.asked("agent", ["a,b"], None) == proofs.asked("agent", ["a", "b"], None)
        assert proofs.asked("agent", [], None) == proofs.asked("agent", [""], None)

        # None of them can arrive. `AuthorityIssuer.IsUsableSubjectName` allows a name only out of
        # `[A-Za-z0-9._ -]` and `IsUsableProfileName` a profile only out of `[A-Za-z0-9_-]`, with
        # no empty one, so the gate answers `400 name` or `400 profile` before it computes a proof
        # at all. `PairingProofTests` pins the same two refusals on the other side.

    def test_every_proof_is_a_full_hmac(self) -> None:
        assert len(proofs.client(TOKEN, BINDING, NONCE)) == proofs.LENGTH

    def test_comparing_proofs_does_not_depend_on_where_they_differ(self) -> None:
        one = proofs.client(TOKEN, BINDING, NONCE)
        assert proofs.matches(one, one)
        assert not proofs.matches(one, bytes(proofs.LENGTH))
