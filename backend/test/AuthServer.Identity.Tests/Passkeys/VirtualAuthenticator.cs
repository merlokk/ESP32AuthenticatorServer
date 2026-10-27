using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuthServer.Identity.Tests.Passkeys;

// A WebAuthn authenticator in software: one ES256 key, a credential id and a counter the test
// controls. Real formats (CBOR COSE key, authenticator data, DER signature), not a stub.
internal sealed class VirtualAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public VirtualAuthenticator(byte[]? credentialId = null) =>
        CredentialId = credentialId ?? RandomNumberGenerator.GetBytes(32);

    public byte[] CredentialId { get; }

    public uint SignCount { get; set; }

    public byte[] CosePublicKey => EncodeCoseKey();

    public byte[]? UserHandle { get; private set; }

    public string Register(string creationOptionsJson, string origin)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        var root = options.RootElement;

        var challenge = root.GetProperty("challenge").GetString()!;
        var rpId = root.GetProperty("rp").GetProperty("id").GetString()!;

        UserHandle = Base64Url.DecodeFromChars(root.GetProperty("user").GetProperty("id").GetString()!);

        var clientDataJson = ClientData("webauthn.create", challenge, origin);
        var authenticatorData = AuthenticatorData(rpId, attested: true);

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(CredentialId),
            rawId = Base64Url.EncodeToString(CredentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                attestationObject = Base64Url.EncodeToString(AttestationObject(authenticatorData)),
                transports = new[] { "internal" },
            },
            clientExtensionResults = new { },
        });
    }

    public string Assert(string requestOptionsJson, string origin, byte[]? userHandle = null)
    {
        using var options = JsonDocument.Parse(requestOptionsJson);
        var root = options.RootElement;

        var challenge = root.GetProperty("challenge").GetString()!;
        var rpId = root.GetProperty("rpId").GetString()!;

        return Assert(Base64Url.DecodeFromChars(challenge), origin, rpId, userHandle ?? UserHandle);
    }

    public string Assert(byte[] challenge, string origin, string rpId, byte[]? userHandle = null)
    {
        var clientDataJson = ClientData("webauthn.get", Base64Url.EncodeToString(challenge), origin);
        var authenticatorData = AuthenticatorData(rpId, attested: false);

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(CredentialId),
            rawId = Base64Url.EncodeToString(CredentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                signature = Base64Url.EncodeToString(Sign(authenticatorData, clientDataJson)),
                userHandle = userHandle is null ? null : Base64Url.EncodeToString(userHandle),
            },
            clientExtensionResults = new { },
        });
    }

    public byte[] Sign(byte[] authenticatorData, byte[] clientDataJson) =>
        _key.SignData(
            [.. authenticatorData, .. SHA256.HashData(clientDataJson)],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

    public static byte[] ClientData(string type, string base64UrlChallenge, string origin) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type,
            challenge = base64UrlChallenge,
            origin,
            crossOrigin = false,
        }));

    public byte[] AuthenticatorData(string rpId, bool attested)
    {
        const byte UserPresent = 0x01;
        const byte UserVerified = 0x04;
        const byte BackupEligible = 0x08;
        const byte BackedUp = 0x10;
        const byte AttestedCredentialData = 0x40;

        var flags = (byte)(UserPresent | UserVerified | BackupEligible | BackedUp);

        if (attested)
        {
            flags |= AttestedCredentialData;
        }

        var counter = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counter, SignCount);

        var data = new List<byte>();
        data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        data.Add(flags);
        data.AddRange(counter);

        if (attested)
        {
            data.AddRange(new byte[16]);

            var length = new byte[2];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);

            data.AddRange(length);
            data.AddRange(CredentialId);
            data.AddRange(CosePublicKey);
        }

        return [.. data];
    }

    private static byte[] AttestationObject(byte[] authenticatorData)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);

        writer.WriteStartMap(3);

        writer.WriteTextString("fmt");
        writer.WriteTextString("none");

        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();

        writer.WriteTextString("authData");
        writer.WriteByteString(authenticatorData);

        writer.WriteEndMap();

        return writer.Encode();
    }

    private byte[] EncodeCoseKey()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);

        writer.WriteStartMap(5);

        writer.WriteInt32(1);
        writer.WriteInt32(2);

        writer.WriteInt32(3);
        writer.WriteInt32(-7);

        writer.WriteInt32(-1);
        writer.WriteInt32(1);

        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Q.X!);

        writer.WriteInt32(-3);
        writer.WriteByteString(parameters.Q.Y!);

        writer.WriteEndMap();

        return writer.Encode();
    }

    public void Dispose() => _key.Dispose();
}
