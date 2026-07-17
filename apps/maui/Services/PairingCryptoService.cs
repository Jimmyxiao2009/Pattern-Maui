using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Pattern.Maui.Services;

/// <summary>
/// Implements the v2 pairing envelope shared with the archived client:
/// X25519 key agreement + XChaCha20-Poly1305. The long-lived private key is
/// returned to the caller so it can be kept in SecureStorage, never in a QR
/// code or normal preferences.
/// </summary>
public static class PairingCryptoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("pattern-pair-v2");

    public sealed record PairingRequest(string Code, string PrivateKey);

    private sealed record SecureRequest(int Version, string Kind, string DeviceId, string PublicKey);
    private sealed record SecureResponse(int Version, string Kind, string DeviceId, string PublicKey, string Nonce, string Ciphertext);

    public static PairingRequest CreateRequest(string deviceId)
    {
        var privateKey = RandomNumberGenerator.GetBytes(32);
        var privateParameters = new X25519PrivateKeyParameters(privateKey, 0);
        var request = new SecureRequest(2, "request", deviceId, B64(privateParameters.GeneratePublicKey().GetEncoded()));
        return new PairingRequest(Wrap(request), B64(privateKey));
    }

    public static string CreateResponse(string requestCode, object payload, string desktopDeviceId)
    {
        var request = Decode<SecureRequest>(Unwrap(requestCode));
        if (request.Version != 2 || request.Kind != "request" || string.IsNullOrWhiteSpace(request.PublicKey))
            throw new InvalidOperationException("无效的安全配对请求");
        var privateKey = RandomNumberGenerator.GetBytes(32);
        var privateParameters = new X25519PrivateKeyParameters(privateKey, 0);
        var peer = new X25519PublicKeyParameters(Unb64(request.PublicKey), 0);
        var shared = new byte[32];
        privateParameters.GenerateSecret(peer, shared, 0);
        var key = Sha256(shared.Concat(Context).ToArray());
        var nonce = RandomNumberGenerator.GetBytes(24);
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        var cipher = XChaCha(true, key, nonce, plain);
        var response = new SecureResponse(2, "response", desktopDeviceId, B64(privateParameters.GeneratePublicKey().GetEncoded()), B64(nonce), B64(cipher));
        return Wrap(response);
    }

    public static JsonElement OpenResponse(string responseCode, string privateKey)
    {
        var response = Decode<SecureResponse>(Unwrap(responseCode));
        if (response.Version != 2 || response.Kind != "response") throw new InvalidOperationException("无效的安全配对响应");
        var local = new X25519PrivateKeyParameters(Unb64(privateKey), 0);
        var peer = new X25519PublicKeyParameters(Unb64(response.PublicKey), 0);
        var shared = new byte[32];
        local.GenerateSecret(peer, shared, 0);
        var key = Sha256(shared.Concat(Context).ToArray());
        var plain = XChaCha(false, key, Unb64(response.Nonce), Unb64(response.Ciphertext));
        using var document = JsonDocument.Parse(plain);
        return document.RootElement.Clone();
    }

    private static string Wrap<T>(T value) => $"pattern://pair?data={Uri.EscapeDataString(B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions))))}";

    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(Unb64(value)), JsonOptions)
        ?? throw new InvalidOperationException("配对数据为空");

    private static string Unwrap(string value)
    {
        var trimmed = value.Trim();
        const string marker = "pattern://pair?data=";
        if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[marker.Length..];
        return Uri.UnescapeDataString(trimmed);
    }

    private static byte[] Sha256(byte[] value) => SHA256.HashData(value);
    private static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Unb64(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

    private static byte[] XChaCha(bool encrypt, byte[] key, byte[] nonce, byte[] input)
    {
        if (key.Length != 32 || nonce.Length != 24) throw new InvalidOperationException("XChaCha 参数长度无效");
        var subKey = HChaCha20(key, nonce.AsSpan(0, 16));
        var nonce12 = new byte[12];
        nonce.AsSpan(16, 8).CopyTo(nonce12.AsSpan(4));
        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
        cipher.Init(encrypt, new AeadParameters(new KeyParameter(subKey), 128, nonce12));
        var output = new byte[cipher.GetOutputSize(input.Length)];
        var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        length += cipher.DoFinal(output, length);
        return output.AsSpan(0, length).ToArray();
    }

    private static byte[] HChaCha20(byte[] key, ReadOnlySpan<byte> nonce)
    {
        var state = new uint[16];
        state[0] = 0x61707865; state[1] = 0x3320646e; state[2] = 0x79622d32; state[3] = 0x6b206574;
        for (var i = 0; i < 8; i++) state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4, 4));
        for (var i = 0; i < 4; i++) state[12 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(i * 4, 4));
        for (var i = 0; i < 10; i++)
        {
            QuarterRound(state, 0, 4, 8, 12); QuarterRound(state, 1, 5, 9, 13); QuarterRound(state, 2, 6, 10, 14); QuarterRound(state, 3, 7, 11, 15);
            QuarterRound(state, 0, 5, 10, 15); QuarterRound(state, 1, 6, 11, 12); QuarterRound(state, 2, 7, 8, 13); QuarterRound(state, 3, 4, 9, 14);
        }
        var output = new byte[32];
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(i * 4, 4), state[i]);
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan((i + 4) * 4, 4), state[i + 12]);
        return output;
    }

    private static void QuarterRound(uint[] s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] = BitOperations.RotateLeft(s[d] ^ s[a], 16);
        s[c] += s[d]; s[b] = BitOperations.RotateLeft(s[b] ^ s[c], 12);
        s[a] += s[b]; s[d] = BitOperations.RotateLeft(s[d] ^ s[a], 8);
        s[c] += s[d]; s[b] = BitOperations.RotateLeft(s[b] ^ s[c], 7);
    }
}
