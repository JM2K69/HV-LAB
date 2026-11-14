using System.Security.Cryptography;
using System.Text;

namespace HVLab.Services;

/// <summary>
/// SHA-512 crypt ("$6$", Ulrich Drepper's specification, the glibc default): the cloud-init seed carries this hash
/// instead of the password in clear, so the CIDATA disk attached to a clone does not reveal it.
/// </summary>
public static class Sha512Crypt
{
    private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int DefaultRounds = 5000;

    /// <summary>Hash with a random 16-character salt.</summary>
    public static string Hash(string password)
    {
        var salt = new char[16];
        for (var i = 0; i < salt.Length; i++) salt[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return Hash(password, new string(salt));
    }

    /// <summary>Hash with a given salt (at most 16 characters are used) and the default 5000 rounds.</summary>
    public static string Hash(string password, string salt)
    {
        if (salt.Length > 16) salt = salt[..16];
        var p = Encoding.UTF8.GetBytes(password);
        var s = Encoding.UTF8.GetBytes(salt);

        var b = Sha(p, s, p);

        using var a = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        a.AppendData(p);
        a.AppendData(s);
        int count;
        for (count = p.Length; count > 64; count -= 64) a.AppendData(b);
        a.AppendData(b, 0, count);
        for (count = p.Length; count > 0; count >>= 1) a.AppendData((count & 1) != 0 ? b : p);
        var c = a.GetHashAndReset();

        using var dp = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        for (var i = 0; i < p.Length; i++) dp.AppendData(p);
        var pSeq = Repeat(dp.GetHashAndReset(), p.Length);

        using var ds = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        for (var i = 0; i < 16 + c[0]; i++) ds.AppendData(s);
        var sSeq = Repeat(ds.GetHashAndReset(), s.Length);

        using var round = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        for (var i = 0; i < DefaultRounds; i++)
        {
            round.AppendData((i & 1) != 0 ? pSeq : c);
            if (i % 3 != 0) round.AppendData(sSeq);
            if (i % 7 != 0) round.AppendData(pSeq);
            round.AppendData((i & 1) != 0 ? c : pSeq);
            c = round.GetHashAndReset();
        }

        var result = new StringBuilder("$6$").Append(salt).Append('$');
        int[] order = [0, 21, 42, 22, 43, 1, 44, 2, 23, 3, 24, 45, 25, 46, 4, 47, 5, 26, 6, 27, 48, 28, 49, 7, 50, 8, 29,
                       9, 30, 51, 31, 52, 10, 53, 11, 32, 12, 33, 54, 34, 55, 13, 56, 14, 35, 15, 36, 57, 37, 58, 16,
                       59, 17, 38, 18, 39, 60, 40, 61, 19, 62, 20, 41];
        for (var i = 0; i < order.Length; i += 3) Encode(result, c[order[i]], c[order[i + 1]], c[order[i + 2]], 4);
        Encode(result, 0, 0, c[63], 2);
        return result.ToString();
    }

    private static byte[] Sha(params byte[][] parts)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        foreach (var part in parts) h.AppendData(part);
        return h.GetHashAndReset();
    }

    /// <summary>The 64-byte digest repeated (or cut) to <paramref name="length"/> bytes.</summary>
    private static byte[] Repeat(byte[] digest, int length)
    {
        var seq = new byte[length];
        for (var i = 0; i < length; i++) seq[i] = digest[i % 64];
        return seq;
    }

    private static void Encode(StringBuilder sb, byte b2, byte b1, byte b0, int n)
    {
        var w = (b2 << 16) | (b1 << 8) | b0;
        for (var i = 0; i < n; i++) { sb.Append(Alphabet[w & 0x3f]); w >>= 6; }
    }
}
