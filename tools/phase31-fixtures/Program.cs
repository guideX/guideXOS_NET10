using System.Security.Cryptography;
using System.Text;

if (args.Length != 2) throw new ArgumentException("input-directory output-file");
string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
byte[] leaf = File.ReadAllBytes(Path.Combine(input, "leaf.der"));
byte[] intermediate = File.ReadAllBytes(Path.Combine(input, "intermediate.der"));
byte[] root = File.ReadAllBytes(Path.Combine(input, "root.der"));
byte[] signature = File.ReadAllBytes(Path.Combine(input, "ske-signature.der"));

static byte[] Join(params byte[][] values)
{
    int length = 0;
    foreach (byte[] value in values) length = checked(length + value.Length);
    byte[] result = new byte[length];
    int offset = 0;
    foreach (byte[] value in values)
    {
        value.CopyTo(result, offset);
        offset += value.Length;
    }
    return result;
}

static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];
static byte[] U24(int value) => [(byte)(value >> 16), (byte)(value >> 8), (byte)value];

static byte[] Hmac(byte[] key, byte[] data)
{
    using HMACSHA256 hmac = new(key);
    return hmac.ComputeHash(data);
}

static byte[] Prf(byte[] secret, string label, byte[] seed, int length)
{
    byte[] labelSeed = Join(Encoding.ASCII.GetBytes(label), seed);
    byte[] a = labelSeed;
    byte[] result = new byte[length];
    int offset = 0;
    while (offset < length)
    {
        a = Hmac(secret, a);
        byte[] block = Hmac(secret, Join(a, labelSeed));
        int count = Math.Min(block.Length, length - offset);
        Array.Copy(block, 0, result, offset, count);
        offset += count;
    }
    return result;
}

static byte[] PlainRecord(byte type, byte[] fragment) =>
    Join([type, 3, 3], U16(fragment.Length), fragment);

static byte[] ProtectedRecord(byte[] key, byte[] fixedIv, ulong sequence,
                              byte type, byte[] plaintext, byte[] explicitNonce)
{
    byte[] seq = new byte[8];
    for (int index = 0; index != 8; ++index)
        seq[index] = (byte)(sequence >> (56 - index * 8));
    byte[] aad = Join(seq, [type, 3, 3], U16(plaintext.Length));
    byte[] nonce = Join(fixedIv, explicitNonce);
    byte[] ciphertext = new byte[plaintext.Length];
    byte[] tag = new byte[16];
    using AesGcm aes = new(key, 16);
    aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
    return PlainRecord(type, Join(explicitNonce, ciphertext, tag));
}

static byte[] Hash(byte[] value) => SHA256.HashData(value);

static string Hex(byte[] value)
{
    StringBuilder builder = new();
    for (int index = 0; index < value.Length; index += 16)
    {
        int count = Math.Min(16, value.Length - index);
        builder.Append("        ");
        for (int inner = 0; inner < count; ++inner)
        {
            if (inner != 0) builder.Append(", ");
            builder.Append($"0x{value[index + inner]:X2}");
        }
        builder.AppendLine(",");
    }
    return builder.ToString();
}

static void Emit(StringBuilder output, string name, byte[] value)
{
    output.AppendLine($"    internal static readonly byte[] {name} =");
    output.AppendLine("    {");
    output.Append(Hex(value));
    output.AppendLine("    };");
}

byte[] clientRandom = [
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31];
byte[] serverRandom = [
    0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
    0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF,
    0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7,
    0xB8, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF];
byte[] hostname = Encoding.ASCII.GetBytes("www.example.com");
byte[] serverPoint = [
    0x04, 0xDB, 0x69, 0x70, 0x92, 0x3E, 0xE6, 0xBE, 0x3A, 0x7D, 0xCB,
    0x05, 0xD4, 0x93, 0x5B, 0x93, 0xC4, 0xDA, 0x5D, 0x0C, 0x55, 0xA8,
    0x5C, 0x5E, 0x10, 0x62, 0x11, 0xEC, 0x3B, 0x18, 0x62, 0xC4, 0xC9,
    0xE3, 0x15, 0x63, 0x1A, 0xC1, 0xC0, 0x5E, 0xB3, 0x01, 0x5F, 0x8B,
    0x68, 0x9C, 0x7B, 0x07, 0x06, 0x66, 0xD0, 0x6F, 0xD5, 0x17, 0x95,
    0x60, 0x99, 0xF7, 0x8E, 0x77, 0xF4, 0xEA, 0xE8, 0xFA, 0x3C];
byte[] clientPublic = [
    0x04, 0x6B, 0x17, 0xD1, 0xF2, 0xE1, 0x2C, 0x42, 0x47, 0xF8, 0xBC,
    0xE6, 0xE5, 0x63, 0xA4, 0x40, 0xF2, 0x77, 0x03, 0x7D, 0x81, 0x2D,
    0xEB, 0x33, 0xA0, 0xF4, 0xA1, 0x39, 0x45, 0xD8, 0x98, 0xC2, 0x96,
    0x4F, 0xE3, 0x42, 0xE2, 0xFE, 0x1A, 0x7F, 0x9B, 0x8E, 0xE7, 0xEB,
    0x4A, 0x7C, 0x0F, 0x9E, 0x16, 0x2B, 0xCE, 0x33, 0x57, 0x6B, 0x31,
    0x5E, 0xCE, 0xCB, 0xB6, 0x40, 0x68, 0x37, 0xBF, 0x51, 0xF5];
byte[] parameters = Join([3, 0, 23, 65], serverPoint);
byte[] sniData = Join(U16(3 + hostname.Length), [0], U16(hostname.Length), hostname);
byte[] extensions = Join(
    [0, 0], U16(5 + hostname.Length), sniData,
    [0, 10], U16(4), [0, 2, 0, 23],
    [0, 11], U16(2), [1, 0],
    [0, 13], U16(4), [0, 2, 4, 3],
    [0, 23], U16(0));
byte[] helloBody = Join([3, 3], clientRandom, [0], U16(2), [0xC0, 0x2B, 1, 0],
                        U16(extensions.Length), extensions);
byte[] clientHello = Join([1], U24(helloBody.Length), helloBody);
byte[] serverHelloBody = Join([3, 3], serverRandom, [0, 0xC0, 0x2B, 0],
                              U16(4), [0, 23, 0, 0]);
byte[] serverHello = Join([2], U24(serverHelloBody.Length), serverHelloBody);
byte[] certificateBody = Join(U24(3 + leaf.Length + 3 + intermediate.Length),
                              U24(leaf.Length), leaf, U24(intermediate.Length),
                              intermediate);
byte[] certificate = Join([11], U24(certificateBody.Length), certificateBody);
byte[] skeBody = Join(parameters, [4, 3], U16(signature.Length), signature);
byte[] serverKeyExchange = Join([12], U24(skeBody.Length), skeBody);
byte[] serverHelloDone = [14, 0, 0, 0];
byte[] clientKeyExchange = Join([16, 0, 0, 66, 65], clientPublic);
byte[] transcriptThroughCke = Join(clientHello, serverHello, certificate,
                                   serverKeyExchange, serverHelloDone,
                                   clientKeyExchange);
byte[] sessionHash = Hash(transcriptThroughCke);
byte[] premaster = serverPoint[1..33];
byte[] master = Prf(premaster, "extended master secret", sessionHash, 48);
byte[] keyBlock = Prf(master, "key expansion", Join(serverRandom, clientRandom), 40);
byte[] clientKey = keyBlock[0..16];
byte[] serverKey = keyBlock[16..32];
byte[] clientIv = keyBlock[32..36];
byte[] serverIv = keyBlock[36..40];
byte[] clientVerify = Prf(master, "client finished", sessionHash, 12);
byte[] clientFinished = Join([20, 0, 0, 12], clientVerify);
byte[] serverVerify = Prf(master, "server finished",
                          Hash(Join(transcriptThroughCke, clientFinished)), 12);
byte[] serverFinished = Join([20, 0, 0, 12], serverVerify);
byte[] clientFinishedRecord = ProtectedRecord(clientKey, clientIv, 0, 22,
                                              clientFinished, new byte[8]);
byte[] serverFinishedRecord = ProtectedRecord(serverKey, serverIv, 0, 22,
                                              serverFinished,
                                              [0, 0, 0, 0, 0, 0, 0, 0x2A]);
byte[] serverApplicationRecord = ProtectedRecord(serverKey, serverIv, 1, 23,
                                                  Encoding.ASCII.GetBytes("PONG"),
                                                  [0, 0, 0, 0, 0, 0, 0, 0x2B]);
byte[] changeCipherSpec = PlainRecord(20, [1]);

StringBuilder source = new();
source.AppendLine("namespace GuideXOS.Net10.ManagedKernel;");
source.AppendLine();
source.AppendLine("/* Deterministic Phase 31 fixture generated by the host-only reference");
source.AppendLine("   generator. The peer chain omits the configured root. */");
source.AppendLine("internal static class ManagedTls12Phase31Fixtures");
source.AppendLine("{");
foreach ((string name, byte[] value) item in new[] {
    ("ClientRandom", clientRandom), ("ServerRandom", serverRandom),
    ("ClientHello", clientHello), ("ServerHello", serverHello),
    ("CertificateMessage", certificate), ("ServerKeyExchange", serverKeyExchange),
    ("ServerHelloDone", serverHelloDone), ("ClientKeyExchange", clientKeyExchange),
    ("ClientFinished", clientFinished), ("ServerFinished", serverFinished),
    ("ClientFinishedRecord", clientFinishedRecord),
    ("ServerFinishedRecord", serverFinishedRecord),
    ("ServerApplicationRecord", serverApplicationRecord),
    ("ChangeCipherSpec", changeCipherSpec), ("Root", root), ("Leaf", leaf),
    ("Intermediate", intermediate), ("ClientPublicKey", clientPublic),
    ("PremasterSecret", premaster), ("SessionHash", sessionHash),
    ("MasterSecret", master), ("KeyBlock", keyBlock),
    ("ClientVerifyData", clientVerify), ("ServerVerifyData", serverVerify) })
    Emit(source, item.name, item.value);
Emit(source, "ServerHelloRecord", PlainRecord(22, serverHello));
int chunk = (certificate.Length + 9) / 10;
for (int index = 0; index != 10; ++index)
{
    int start = index * chunk;
    byte[] part = start >= certificate.Length
        ? [] : certificate[start..Math.Min(certificate.Length, start + chunk)];
    Emit(source, $"CertificateRecord{index}", PlainRecord(22, part));
}
Emit(source, "ServerKeyExchangeRecord", PlainRecord(22, serverKeyExchange));
Emit(source, "ServerHelloDoneRecord", PlainRecord(22, serverHelloDone));
source.AppendLine("    internal const int ServerRecordCount = 13;");
source.AppendLine();
source.AppendLine("    internal static byte[] GetServerRecord(int index) => index switch");
source.AppendLine("    {");
source.AppendLine("        0 => ServerHelloRecord,");
for (int index = 0; index != 10; ++index)
    source.AppendLine($"        {index + 1} => CertificateRecord{index},");
source.AppendLine("        11 => ServerKeyExchangeRecord,");
source.AppendLine("        12 => ServerHelloDoneRecord,");
source.AppendLine("        _ => System.Array.Empty<byte>()");
source.AppendLine("    };");
source.AppendLine("}");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, source.ToString(), new UTF8Encoding(false));
Console.WriteLine($"PHASE31_FIXTURE_OUTPUT={output}");
