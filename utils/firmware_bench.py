#!/usr/bin/env python3
"""OTA download benchmark: fetches a firmware image the way a device does (device-protocol.md,
Firmware) and times every piece.

  # once, a test device (the first one on an empty server needs no registration window):
  # (/register bypasses the BFF: the backend, or the public server)
  python utils/firmware_bench.py register --url http://localhost:5180 --email me@example.com
  # then, through the BFF (the path a device takes), and the backend directly:
  python utils/firmware_bench.py run --url http://localhost:3100 --board x4pro
  python utils/firmware_bench.py run --url http://localhost:5180 --token-url http://localhost:3100 --runs 3

Modes: keepalive (64 KB Range pieces on one connection, as the firmware does), fresh (a new
connection, TCP + TLS, per piece), whole (one request, no Range). Sign-in goes to --token-url
(default --url), whose origin is the token's issuer: the public origin.

Needs `cryptography` (pip install cryptography) for register and sign-in; not with --token.
The device file holds a private key: keep it out of git.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import http.client
import json
import os
import secrets
import ssl
import statistics
import sys
import time
import urllib.parse
from dataclasses import dataclass, field

PIECE = 64 * 1024


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


# --- the device key, registration, sign-in -------------------------------------------------------


def load_crypto():
    try:
        from cryptography.hazmat.primitives import hashes, serialization
        from cryptography.hazmat.primitives.asymmetric import ec
        from cryptography.hazmat.primitives.asymmetric.utils import decode_dss_signature
    except ImportError:
        sys.exit("needs cryptography: pip install cryptography (or pass --token)")
    return hashes, serialization, ec, decode_dss_signature


def es256(key, header: dict, claims: dict) -> str:
    hashes, _, ec, decode_dss_signature = load_crypto()
    signing_input = f"{b64url(json.dumps(header).encode())}.{b64url(json.dumps(claims).encode())}"
    r, s = decode_dss_signature(key.sign(signing_input.encode(), ec.ECDSA(hashes.SHA256())))
    return f"{signing_input}.{b64url(r.to_bytes(32, 'big') + s.to_bytes(32, 'big'))}"


def load_key(pem: str):
    _, serialization, _, _ = load_crypto()
    return serialization.load_pem_private_key(pem.encode(), password=None)


class Client:
    """http(s) to one origin, one connection kept until closed or the server closes it."""

    def __init__(self, url: str, insecure: bool, timeout: float):
        parts = urllib.parse.urlsplit(url)
        self.scheme, self.host = parts.scheme, parts.netloc
        self.base = parts.path.rstrip("/")
        self.timeout = timeout
        self.context = ssl._create_unverified_context() if insecure else ssl.create_default_context()
        self.conn: http.client.HTTPConnection | None = None
        self.connects = 0
        self.connect_seconds = 0.0

    def open(self) -> None:
        if self.scheme == "https":
            conn = http.client.HTTPSConnection(self.host, timeout=self.timeout, context=self.context)
        else:
            conn = http.client.HTTPConnection(self.host, timeout=self.timeout)
        started = time.perf_counter()
        conn.connect()  # TCP and, on https, the TLS handshake
        self.connect_seconds += time.perf_counter() - started
        self.connects += 1
        self.conn = conn

    def close(self) -> None:
        if self.conn is not None:
            self.conn.close()
            self.conn = None

    def request(self, method: str, path: str, headers: dict, body: bytes | None = None):
        """(response, headers time, body time, body). Opens a connection when there is none."""
        if self.conn is None or self.conn.sock is None:
            self.open()
        started = time.perf_counter()
        self.conn.request(method, self.base + path, body=body, headers=headers)
        response = self.conn.getresponse()
        headed = time.perf_counter()
        data = response.read()
        done = time.perf_counter()
        if response.will_close:
            self.close()
        return response, headed - started, done - headed, data


def register(args) -> None:
    _, serialization, ec, _ = load_crypto()
    key = ec.generate_private_key(ec.SECP256R1())
    point = key.public_key().public_bytes(
        serialization.Encoding.X962, serialization.PublicFormat.UncompressedPoint)
    claims = {
        "email": args.email,
        "serial": secrets.token_hex(16),
        "mac": ":".join(f"{b:02x}" for b in secrets.token_bytes(6)),
        "totp": base64.b32encode(secrets.token_bytes(20)).decode(),
        "pub": b64url(point),
        "iat": int(time.time()),
    }
    jws = es256(key, {"alg": "ES256", "typ": "JWT"}, claims)
    client = Client(args.url, args.insecure, args.timeout)
    response, _, _, data = client.request(
        "POST", "/register", {"Content-Type": "application/jwt"}, jws.encode())
    if response.status != 200:
        sys.exit(f"register: {response.status} {data[:300].decode(errors='replace')}")
    pem = key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
                            serialization.NoEncryption()).decode()
    device = {"clientId": json.loads(data)["clientId"], "key": pem,
              "serial": claims["serial"], "totp": claims["totp"]}
    with open(args.device, "w", encoding="utf-8") as file:
        json.dump(device, file, indent=2)
    print(f"registered {device['clientId']} ({args.email}) -> {args.device}")


def sign_in(client: Client, url: str, device: dict) -> str:
    parts = urllib.parse.urlsplit(url)
    now = int(time.time())
    assertion = es256(
        load_key(device["key"]),
        {"alg": "ES256", "typ": "client-authentication+jwt"},
        {"iss": device["clientId"], "sub": device["clientId"], "aud": f"{parts.scheme}://{parts.netloc}/",
         "jti": secrets.token_hex(16), "iat": now, "exp": now + 300},
    )
    form = urllib.parse.urlencode({
        "grant_type": "client_credentials",
        "client_id": device["clientId"],
        "client_assertion_type": "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        "client_assertion": assertion,
    }).encode()
    response, _, _, data = client.request(
        "POST", "/connect/token", {"Content-Type": "application/x-www-form-urlencoded"}, form)
    if response.status != 200:
        sys.exit(f"sign-in: {response.status} {data[:300].decode(errors='replace')}")
    return json.loads(data)["access_token"]


# --- the benchmark --------------------------------------------------------------------------------


@dataclass
class Run:
    mode: str
    seconds: float = 0.0
    bytes: int = 0
    connects: int = 0
    connect_seconds: float = 0.0
    headers: list[float] = field(default_factory=list)  # request sent -> status and headers
    bodies: list[float] = field(default_factory=list)  # headers -> last byte
    chunked: bool = False
    sha_ok: bool = False


def download(client: Client, path: str, token: str, release: dict, mode: str, piece: int) -> Run:
    run = Run(mode)
    digest = hashlib.sha256()
    size = release["size"]
    auth = {"Authorization": f"Bearer {token}"}
    client.close()
    client.connects, client.connect_seconds = 0, 0.0
    started = time.perf_counter()
    if mode == "whole":
        response, headed, body, data = client.request("GET", path, auth)
        if response.status != 200:
            sys.exit(f"whole: {response.status} {data[:300].decode(errors='replace')}")
        run.headers.append(headed)
        run.bodies.append(body)
        run.chunked = response.getheader("Transfer-Encoding", "").lower() == "chunked"
        digest.update(data)
        run.bytes = len(data)
    else:
        offset = 0
        while offset < size:
            last = min(offset + piece, size) - 1
            if mode == "fresh":
                client.close()
            response, headed, body, data = client.request(
                "GET", path, {**auth, "Range": f"bytes={offset}-{last}"})
            if response.status != 206:
                sys.exit(f"piece {offset}: {response.status} {data[:300].decode(errors='replace')}")
            run.headers.append(headed)
            run.bodies.append(body)
            run.chunked = run.chunked or response.getheader("Transfer-Encoding", "").lower() == "chunked"
            digest.update(data)
            offset += len(data)
        run.bytes = offset
    run.seconds = time.perf_counter() - started
    run.connects, run.connect_seconds = client.connects, client.connect_seconds
    run.sha_ok = digest.hexdigest() == release["sha256"]
    return run


def ms(values: list[float], q: float) -> str:
    if not values:
        return "-"
    ordered = sorted(values)
    return f"{ordered[min(len(ordered) - 1, int(q * len(ordered)))] * 1000:.1f}"


def report(runs: list[Run]) -> None:
    print(f"\n{'mode':<10}{'time s':>8}{'KB/s':>9}{'req':>5}{'conn':>5}{'conn ms':>9}"
          f"{'hdr p50':>9}{'hdr p95':>9}{'hdr max':>9}{'body p50':>9}  sha  chunked")
    for run in runs:
        print(f"{run.mode:<10}{run.seconds:>8.2f}{run.bytes / 1024 / run.seconds:>9.0f}{len(run.headers):>5}"
              f"{run.connects:>5}{run.connect_seconds * 1000 / max(run.connects, 1):>9.1f}"
              f"{ms(run.headers, .5):>9}{ms(run.headers, .95):>9}{ms(run.headers, 1):>9}{ms(run.bodies, .5):>9}"
              f"  {'ok ' if run.sha_ok else 'BAD'}  {'yes' if run.chunked else 'no'}")
    print("hdr: request sent -> headers received (server time + RTT); body: headers -> last byte; "
          "conn ms: TCP + TLS per connection")


def bench(args) -> None:
    url = args.url
    client = Client(url, args.insecure, args.timeout)
    token = args.token
    if token is None:
        with open(args.device, encoding="utf-8") as file:
            device = json.load(file)
        token_url = args.token_url or url
        token = sign_in(Client(token_url, args.insecure, args.timeout), token_url, device)

    if args.release:
        query = ""
    else:
        query = f"?board={urllib.parse.quote(args.board)}" if args.board else ""
    response, _, _, data = client.request("GET", f"/api/device/firmware{query}", {"Authorization": f"Bearer {token}"})
    if response.status != 200:
        sys.exit(f"firmware list: {response.status} {data[:300].decode(errors='replace')}")
    releases = json.loads(data)["releases"]
    if args.release:
        releases = [release for release in releases if release["id"] == args.release]
    else:
        releases = sorted(releases, key=lambda release: not release["active"])
    if not releases:
        sys.exit("no such release (upload one: Admin -> Firmware)")
    release = releases[0]
    print(f"{url}: {release['board']} {release['version']}, {release['size']} bytes, "
          f"piece {args.piece} -> {-(-release['size'] // args.piece)} requests")

    path = f"/api/device/firmware/{release['id']}/image"
    runs = [download(client, path, token, release, mode, args.piece)
            for _ in range(args.runs) for mode in args.modes.split(",")]
    report(runs)
    if args.runs > 1:
        print("\nmean per mode:")
        for mode in args.modes.split(","):
            same = [run for run in runs if run.mode == mode]
            print(f"  {mode:<10}{statistics.mean(run.seconds for run in same):>8.2f} s"
                  f"{statistics.mean(run.bytes / 1024 / run.seconds for run in same):>9.0f} KB/s")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--insecure", action="store_true", help="skip TLS certificate checks")
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--device", default=os.path.join(os.path.dirname(__file__), "bench-device.json"),
                        help="the device file register writes (default utils/bench-device.json)")
    commands = parser.add_subparsers(dest="command", required=True)

    reg = commands.add_parser("register", help="register a test device (POST /register)")
    reg.add_argument("--url", required=True, help="the backend or the public server, e.g. http://localhost:5180")
    reg.add_argument("--email", required=True, help="an account with an open registration window")

    run = commands.add_parser("run", help="download a release and time it")
    run.add_argument("--url", required=True, help="where to download from: the BFF or the backend")
    run.add_argument("--token-url", help="where to sign in, the public origin (default --url)")
    run.add_argument("--token", help="a device access token instead of signing in with the device file")
    run.add_argument("--board", help="the board's active release (default: any board's)")
    run.add_argument("--release", help="a release id")
    run.add_argument("--piece", type=int, default=PIECE, help=f"Range size (default {PIECE}, the firmware's)")
    run.add_argument("--modes", default="keepalive,fresh,whole")
    run.add_argument("--runs", type=int, default=1)

    args = parser.parse_args()
    (register if args.command == "register" else bench)(args)


if __name__ == "__main__":
    main()
